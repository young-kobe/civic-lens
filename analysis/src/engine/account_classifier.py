"""
Account tier classifier for Civic Lens (walkthrough 036).

Classifies each author we see in our political-content sample into one of
three tiers — elected_official, affiliated, general_public — and persists
affirmative classifications to ``account_profiles``. Absence from the table
means "general_public" by default.

Two classification paths, priority curated > llm:

1. Curated list (``data/known_accounts.yaml``). Deterministic, high trust.
   Re-running overwrites any existing row for the same handle, so YAML
   edits are the source of truth.

2. LLM classifier. Runs for X authors who appear in ``x_posts_raw`` with
   meaningful volume AND are not already classified. Uses the author's
   display name, self-description, verified flag, follower count, and up
   to 5 recent posts as evidence. Skipped entirely when ``llm_enabled``
   is False.

Reddit classification is intentionally not implemented — electeds and
formal political orgs are rare on Reddit, and the equivalent signal does
not exist in the data we collect.
"""

from __future__ import annotations

import sqlite3
import time
from dataclasses import dataclass
from pathlib import Path
from typing import List, Optional

import yaml

from analysis.src.common.logger import get_logger
from analysis.src.llm.prompts import (
    ACCOUNT_CLASSIFIER_SYSTEM_PROMPT,
    ACCOUNT_CLASSIFIER_USER_PROMPT_TEMPLATE,
)
from analysis.src.llm.schemas import ACCOUNT_CLASSIFIER_SCHEMA

logger = get_logger(__name__)


# Minimum post volume from an author in the last lookback window before we
# spend LLM tokens on them. Lower = more coverage, higher = fewer calls.
MIN_POSTS_FOR_LLM_CLASSIFICATION = 3
LLM_LOOKBACK_SECONDS = 30 * 24 * 60 * 60  # 30 days
# Hard cap per run so one misconfiguration can't burn through a quota.
MAX_LLM_CLASSIFICATIONS_PER_RUN = 200

# Tiers persisted to account_profiles. "general_public" is the implicit default
# (no row); it is written only when the LLM affirmatively picks it — that lets
# us skip re-classifying the same author on every run.
ALLOWED_TIERS = {"elected_official", "affiliated", "general_public"}
VALID_CLASSIFICATION_METHODS = {"curated_list", "llm"}


@dataclass
class ClassificationResult:
    tier: str
    confidence: Optional[float]
    method: str  # 'curated_list' | 'llm'
    reasoning: Optional[str] = None


@dataclass
class CuratedEntry:
    """One row's worth of curated data, normalized across YAML shapes."""
    handle: str
    tier: str
    full_name: Optional[str] = None
    party: Optional[str] = None
    branch: Optional[str] = None
    chamber: Optional[str] = None
    state_or_district: Optional[str] = None
    office_title: Optional[str] = None
    account_type: Optional[str] = None
    notes: Optional[str] = None


def _strip_handle(raw: Optional[str]) -> str:
    """Normalize a YAML 'handle' value — strip leading @ and whitespace."""
    if not raw:
        return ""
    return str(raw).strip().lstrip("@")


def _parse_curated_yaml(payload: dict) -> List[CuratedEntry]:
    """Parse either the rich nested format (accounts.executive_branch + accounts.congress)
    or the legacy flat format (top-level elected_official/affiliated keys).

    Returns a flat list of CuratedEntry rows, one per handle.
    """
    entries: List[CuratedEntry] = []

    # Rich nested format (data/known_political_x_accounts.yaml).
    accounts_block = payload.get("accounts") or {}
    if isinstance(accounts_block, dict):
        # Executive: each person has a list of handles (official / personal / institutional).
        for person in accounts_block.get("executive_branch") or []:
            if not isinstance(person, dict):
                continue
            full_name = person.get("name")
            office_title = person.get("office")
            raw_branch = (person.get("branch") or "").strip().lower()
            branch = raw_branch if raw_branch in {"executive", "legislative", "judicial"} else None
            notes = person.get("notes")
            for handle_entry in person.get("accounts") or []:
                if not isinstance(handle_entry, dict):
                    continue
                handle = _strip_handle(handle_entry.get("handle"))
                if not handle:
                    continue
                entries.append(CuratedEntry(
                    handle=handle,
                    tier="elected_official",
                    full_name=full_name,
                    branch=branch,
                    office_title=office_title,
                    account_type=handle_entry.get("account_type"),
                    notes=notes,
                ))

        # Congress: nested under accounts.congress.{house,senate}, one row per person.
        congress_block = accounts_block.get("congress") or {}
        if isinstance(congress_block, dict):
            for chamber_key, chamber_label in (("house", "house"), ("senate", "senate")):
                for person in congress_block.get(chamber_key) or []:
                    if not isinstance(person, dict):
                        continue
                    handle = _strip_handle(person.get("handle"))
                    if not handle:
                        continue
                    office_title = "Senator" if chamber_label == "senate" else "Representative"
                    entries.append(CuratedEntry(
                        handle=handle,
                        tier="elected_official",
                        full_name=person.get("name"),
                        party=person.get("party"),
                        branch="legislative",
                        chamber=chamber_label,
                        state_or_district=person.get("state_or_district"),
                        office_title=office_title,
                        account_type=None,
                    ))

    # Legacy flat format (data/known_accounts.yaml) — still honored so we don't
    # lose the small affiliated list (RNC, DNC, think tanks) that shipped with 036.
    for tier in ("elected_official", "affiliated"):
        for row in payload.get(tier) or []:
            if not isinstance(row, dict):
                continue
            handle = _strip_handle(row.get("handle"))
            if not handle:
                continue
            entries.append(CuratedEntry(
                handle=handle,
                tier=tier,
                notes=row.get("notes"),
            ))

    return entries


class AccountClassifier:
    """Curated-list + LLM-backed author tier classifier."""

    def __init__(self, db_path: str, llm_enabled: bool = False):
        self.db_path = db_path
        self.llm_enabled = llm_enabled
        self._llm_client = None
        if llm_enabled:
            from analysis.src.llm import get_llm_client
            self._llm_client = get_llm_client()
            if not self._llm_client.is_available:
                logger.warning("LLM unavailable; account classifier will run curated-list only")
                self._llm_client = None

    # ---------- Curated list ----------

    def load_curated(self, yaml_path: Path | str) -> dict:
        """Upsert entries from the curated YAML into account_profiles.

        Supports both the rich nested format (accounts.executive_branch +
        accounts.congress.{house,senate}) and the legacy flat format
        (top-level elected_official / affiliated keys). One row per handle —
        executive-branch people with @POTUS + @personal + @institutional
        each produce three rows.

        Returns ``{"elected_official": int, "affiliated": int, "skipped": int}``.
        Re-running overwrites all richer-metadata columns; YAML is the source
        of truth.
        """
        path = Path(yaml_path)
        if not path.exists():
            logger.warning(f"Curated accounts file not found: {path}")
            return {"elected_official": 0, "affiliated": 0, "skipped": 0}

        with path.open("r", encoding="utf-8") as f:
            payload = yaml.safe_load(f) or {}

        entries = _parse_curated_yaml(payload)
        counts = {"elected_official": 0, "affiliated": 0, "skipped": 0}
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        conn.execute("PRAGMA busy_timeout = 5000")
        try:
            cursor = conn.cursor()
            for entry in entries:
                if entry.tier not in counts:
                    counts["skipped"] += 1
                    continue
                author_id = self._resolve_x_author_id(cursor, entry.handle)
                # If we've never ingested the account, still record the
                # classification keyed on the lowercased handle so it's
                # in place when we do see a post from them.
                stored_author_id = author_id or entry.handle.lower()
                display_name = entry.full_name or entry.handle
                cursor.execute(
                    """
                    INSERT INTO account_profiles
                        (platform, author_id, display_name, tier,
                         classification_method, classified_at, confidence,
                         reasoning, notes, full_name, party, branch, chamber,
                         state_or_district, office_title, account_type)
                    VALUES ('x', ?, ?, ?, 'curated_list', ?, NULL, NULL,
                            ?, ?, ?, ?, ?, ?, ?, ?)
                    ON CONFLICT(platform, author_id) DO UPDATE SET
                        display_name = excluded.display_name,
                        tier = excluded.tier,
                        classification_method = excluded.classification_method,
                        classified_at = excluded.classified_at,
                        confidence = NULL,
                        reasoning = NULL,
                        notes = excluded.notes,
                        full_name = excluded.full_name,
                        party = excluded.party,
                        branch = excluded.branch,
                        chamber = excluded.chamber,
                        state_or_district = excluded.state_or_district,
                        office_title = excluded.office_title,
                        account_type = excluded.account_type
                    """,
                    (
                        stored_author_id,
                        display_name,
                        entry.tier,
                        now,
                        entry.notes,
                        entry.full_name,
                        entry.party,
                        entry.branch,
                        entry.chamber,
                        entry.state_or_district,
                        entry.office_title,
                        entry.account_type,
                    ),
                )
                counts[entry.tier] += 1
            conn.commit()
        finally:
            conn.close()

        logger.info(
            f"Curated accounts loaded: "
            f"elected_official={counts['elected_official']}, "
            f"affiliated={counts['affiliated']}, "
            f"skipped={counts['skipped']}"
        )
        return counts

    def _resolve_x_author_id(
        self, cursor: sqlite3.Cursor, handle: str,
    ) -> Optional[str]:
        cursor.execute(
            "SELECT user_id FROM x_users_raw WHERE LOWER(username) = ?",
            (handle.lower(),),
        )
        row = cursor.fetchone()
        return row[0] if row else None

    # ---------- LLM classification ----------

    def classify_with_llm(self, limit: int = MAX_LLM_CLASSIFICATIONS_PER_RUN) -> int:
        """Run the LLM classifier on unclassified X authors that meet the
        volume threshold. Returns count of rows written.
        """
        if not self.llm_enabled or self._llm_client is None:
            logger.info("LLM account classifier skipped (llm disabled or unavailable)")
            return 0

        cutoff = int(time.time()) - LLM_LOOKBACK_SECONDS
        conn = sqlite3.connect(self.db_path)
        conn.execute("PRAGMA busy_timeout = 5000")
        try:
            cursor = conn.cursor()
            candidates = self._load_unclassified_authors(cursor, cutoff, limit)
            logger.info(f"LLM account classification: {len(candidates)} candidates")
            written = 0
            for author_id, handle, display_name, description, verified, followers in candidates:
                samples = self._recent_posts(cursor, author_id, cutoff, k=5)
                if not samples:
                    continue
                result = self._classify_single(
                    platform="x",
                    handle=handle or "",
                    display_name=display_name or handle or "(unknown)",
                    description=description or "",
                    verified=bool(verified),
                    followers_count=followers or 0,
                    post_samples=samples,
                )
                if result is None:
                    continue
                self._persist_llm(cursor, "x", author_id, display_name or handle, result)
                written += 1
                conn.commit()
            logger.info(f"LLM account classification complete: {written} written")
            return written
        finally:
            conn.close()

    def _load_unclassified_authors(
        self,
        cursor: sqlite3.Cursor,
        cutoff: int,
        limit: int,
    ) -> List[tuple]:
        """X authors with >= threshold posts in window, not yet classified."""
        cursor.execute(
            """
            SELECT p.author_id, u.username, u.name, u.description,
                   u.verified, u.followers_count
            FROM x_posts_raw p
            LEFT JOIN x_users_raw u ON u.user_id = p.author_id
            LEFT JOIN account_profiles ap
              ON ap.platform = 'x' AND ap.author_id = p.author_id
            WHERE p.created_at >= ?
              AND ap.author_id IS NULL
            GROUP BY p.author_id
            HAVING COUNT(p.tweet_id) >= ?
            ORDER BY COUNT(p.tweet_id) DESC
            LIMIT ?
            """,
            (cutoff, MIN_POSTS_FOR_LLM_CLASSIFICATION, limit),
        )
        return cursor.fetchall()

    def _recent_posts(
        self, cursor: sqlite3.Cursor, author_id: str, cutoff: int, k: int = 5,
    ) -> List[str]:
        cursor.execute(
            """
            SELECT text FROM x_posts_raw
            WHERE author_id = ? AND created_at >= ?
            ORDER BY created_at DESC LIMIT ?
            """,
            (author_id, cutoff, k),
        )
        return [row[0] for row in cursor.fetchall() if row[0]]

    def _classify_single(
        self,
        platform: str,
        handle: str,
        display_name: str,
        description: str,
        verified: bool,
        followers_count: int,
        post_samples: List[str],
    ) -> Optional[ClassificationResult]:
        if self._llm_client is None:
            return None
        samples_block = "\n".join(
            f"- {post[:240]}" for post in post_samples[:5]
        )
        user_prompt = ACCOUNT_CLASSIFIER_USER_PROMPT_TEMPLATE.format(
            platform=platform,
            handle=handle,
            display_name=display_name or "(none)",
            description=(description or "(none)")[:400],
            verified="yes" if verified else "no",
            followers_count=followers_count,
            post_samples=samples_block or "(no recent posts available)",
        )
        try:
            response = self._llm_client.complete(
                system_prompt=ACCOUNT_CLASSIFIER_SYSTEM_PROMPT,
                user_prompt=user_prompt,
                response_schema=ACCOUNT_CLASSIFIER_SCHEMA,
            )
        except Exception as e:
            logger.warning(f"Account classifier LLM call failed for @{handle}: {e}")
            return None

        tier = response.get("tier")
        if tier not in ALLOWED_TIERS:
            return None
        try:
            confidence = float(response.get("confidence", 0.5))
        except (TypeError, ValueError):
            confidence = 0.5
        confidence = max(0.0, min(1.0, confidence))
        reasoning = response.get("reasoning") or None
        return ClassificationResult(
            tier=tier, confidence=confidence, method="llm", reasoning=reasoning,
        )

    def _persist_llm(
        self,
        cursor: sqlite3.Cursor,
        platform: str,
        author_id: str,
        display_name: Optional[str],
        result: ClassificationResult,
    ) -> None:
        # Only the LLM path uses ON CONFLICT DO NOTHING — we never overwrite
        # an existing classification (curated beats llm; llm doesn't re-run
        # itself to avoid churn). Curated reruns use a separate UPSERT above.
        # Faction columns (party/branch/office_title/etc.) stay NULL — the
        # LLM classifies tier only, not faction metadata.
        cursor.execute(
            """
            INSERT INTO account_profiles
                (platform, author_id, display_name, full_name, tier,
                 classification_method, classified_at, confidence, reasoning)
            VALUES (?, ?, ?, ?, ?, 'llm', ?, ?, ?)
            ON CONFLICT(platform, author_id) DO NOTHING
            """,
            (
                platform,
                author_id,
                display_name,
                display_name,
                result.tier,
                int(time.time()),
                result.confidence,
                result.reasoning,
            ),
        )
