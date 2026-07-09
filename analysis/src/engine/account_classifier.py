"""
Curated account-tier loader for Civic Lens.

Reads ``data/known_political_x_accounts.yaml`` (and the legacy flat shape
that pre-dated it) and writes one row per handle into ``account_profiles``
with ``classification_method='curated_list'``. Re-running overwrites all
metadata columns, so the YAML is the single source of truth.

The earlier LLM-driven classifier path was removed on 2026-04-25. Officials-
tier identification now flows through ``analysis.src.reporting.entity_registry``
(``data/verified_officials.yaml`` lookup) and the curated YAML loader here;
no LLM is involved. Accounts not present in the curated YAML are treated as
``general_public`` by default at the aggregator layer.
"""

from __future__ import annotations

import sqlite3
import time
from dataclasses import dataclass
from pathlib import Path
from typing import List, Optional

import yaml

from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


# Tiers persisted to account_profiles. "general_public" is the implicit
# default (no row); we never write a row for it.
ALLOWED_TIERS = {"elected_official", "affiliated"}


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
                    # Primary handle plus any leadership/personal alternates
                    # (also_handles) — leadership figures carry a second handle
                    # (e.g. @SpeakerJohnson + @RepMikeJohnson) that must resolve
                    # to the same person so both tier systems agree (audit D-1).
                    handles = [handle] + [
                        h for h in (
                            _strip_handle(a) for a in (person.get("also_handles") or [])
                        ) if h
                    ]
                    for member_handle in handles:
                        entries.append(CuratedEntry(
                            handle=member_handle,
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
    """Curated-YAML loader for account_profiles. The class shape is kept
    (rather than collapsing to a free function) so callers can construct
    once at startup with a db_path and load multiple YAMLs in sequence
    without re-passing it."""

    def __init__(self, db_path: str):
        self.db_path = db_path

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
        conn.execute("PRAGMA foreign_keys = ON")  # match Go ingestor (audit D-5)
        try:
            cursor = conn.cursor()
            for entry in entries:
                if entry.tier not in ALLOWED_TIERS:
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
                         classification_method, classified_at,
                         notes, full_name, party, branch, chamber,
                         state_or_district, office_title, account_type)
                    VALUES ('x', ?, ?, ?, 'curated_list', ?,
                            ?, ?, ?, ?, ?, ?, ?, ?)
                    ON CONFLICT(platform, author_id) DO UPDATE SET
                        display_name = excluded.display_name,
                        tier = excluded.tier,
                        classification_method = excluded.classification_method,
                        classified_at = excluded.classified_at,
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

    @staticmethod
    def _resolve_x_author_id(
        cursor: sqlite3.Cursor, handle: str,
    ) -> Optional[str]:
        cursor.execute(
            "SELECT user_id FROM x_users_raw WHERE LOWER(username) = ?",
            (handle.lower(),),
        )
        row = cursor.fetchone()
        return row[0] if row else None
