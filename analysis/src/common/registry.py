"""
analysis/src/common/registry.py — YAML loading, alias canonicalization, and
political-lean flattening for `analysis/src/etl/registry_sync.py`.

Political lean is curated here from editorial YAML and must never be fed
into an LLM prompt (bias/priming risk). Pure YAML parsing + deterministic
string mapping; nothing here talks to an LLM.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import yaml

logger = logging.getLogger(__name__)


# --------------------------------------------------------------------------- #
#  Canonicalizers                                                              #
# --------------------------------------------------------------------------- #

def canonicalize_news_domain(raw: Optional[str]) -> Optional[str]:
    """``www.NYTimes.com`` -> ``nytimes.com``."""
    if not raw:
        return None
    d = raw.strip().lower()
    if d.startswith("www."):
        d = d[4:]
    return d or None


def canonicalize_subreddit(raw: Optional[str]) -> Optional[str]:
    """``r/Politics`` / ``/r/Politics`` / ``Politics`` -> ``politics``."""
    if not raw:
        return None
    s = raw.strip().lower()
    if s.startswith("/r/"):
        s = s[3:]
    elif s.startswith("r/"):
        s = s[2:]
    return s or None


def canonicalize_handle(raw: Optional[str]) -> Optional[str]:
    """``@POTUS`` -> ``potus``. Returns None for empty / bare-``@``."""
    if not raw:
        return None
    h = raw.strip().lower()
    if h.startswith("@"):
        h = h[1:]
    return h or None


# --------------------------------------------------------------------------- #
#  Political-lean flattening: outlet/subreddit YAML's 6-point vocabulary and  #
#  official/account party codes both collapse onto corpus.political_lean's   #
#  5 values. The pre-flattening string is never discarded — callers store    #
#  it verbatim in corpus.entities.lean_source for audit.                     #
# --------------------------------------------------------------------------- #

UNKNOWN_LEAN = "unknown"

# Outlet partisan_lean / subreddit tilt -> corpus.political_lean. Anything
# absent or unrecognized falls back to UNKNOWN_LEAN and is logged, not guessed.
LEAN_SCALE_TO_POLITICAL_LEAN: Dict[str, str] = {
    "left": "democrat",
    "center-left": "democrat",
    "center": "independent",
    "center-right": "republican",
    "right": "republican",
    "mixed": "mixed",
}

# Official/account party code -> corpus.political_lean. "independent-dem"
# maps to "independent" (actual registration; caucus relationship survives
# verbatim in lean_source). "other" has no unambiguous bucket and falls
# through to UNKNOWN_LEAN, logged.
PARTY_CODE_TO_POLITICAL_LEAN: Dict[str, str] = {
    "R": "republican",
    "D": "democrat",
    "I": "independent",
    "INDEPENDENT-DEM": "independent",
}


def flatten_lean_scale(raw: Optional[str], *, context: str) -> str:
    """Flatten an outlet partisan_lean / subreddit tilt string onto
    `corpus.political_lean`. `context` identifies the source (e.g. the
    outlet domain) in the warning log for an unrecognized or absent value."""
    if not raw:
        logger.warning("registry_sync: %s has no lean value; defaulting to unknown", context)
        return UNKNOWN_LEAN
    key = raw.strip().lower()
    mapped = LEAN_SCALE_TO_POLITICAL_LEAN.get(key)
    if mapped is None:
        logger.warning(
            "registry_sync: %s has unrecognized lean value %r; defaulting to unknown",
            context, raw,
        )
        return UNKNOWN_LEAN
    return mapped


def flatten_party_code(raw: Optional[str], *, context: str) -> str:
    """Flatten an official/account party code onto `corpus.political_lean`.
    `context` identifies the source (e.g. the handle) in the warning log for
    an unrecognized or absent value."""
    if not raw:
        logger.warning("registry_sync: %s has no party value; defaulting to unknown", context)
        return UNKNOWN_LEAN
    key = raw.strip().upper()
    mapped = PARTY_CODE_TO_POLITICAL_LEAN.get(key)
    if mapped is None:
        logger.warning(
            "registry_sync: %s has unrecognized party value %r; defaulting to unknown",
            context, raw,
        )
        return UNKNOWN_LEAN
    return mapped


# --------------------------------------------------------------------------- #
#  Registry records — one dataclass per editorial YAML shape.                 #
# --------------------------------------------------------------------------- #

@dataclass(frozen=True)
class OutletRecord:
    """One `data/news_outlets.yaml` entry."""
    entity_key: str                 # canonical domain (primary)
    aliases: Tuple[str, ...]        # canonicalized also_domains
    display_name: str
    blurb: str
    raw_lean: Optional[str]         # YAML partisan_lean, pre-flattening
    source_citation: Optional[str]  # YAML lean_source (the citation, e.g. "AllSides 2024 (Lean Left)")
    owner: Optional[str]


@dataclass(frozen=True)
class OfficialRecord:
    """One `data/verified_officials.yaml` entry."""
    entity_key: str                 # canonical handle (primary)
    aliases: Tuple[str, ...]        # canonicalized also_handles
    display_name: str
    blurb: str
    role_title: str                 # YAML office
    raw_party: Optional[str]        # YAML party, pre-flattening
    term_start: Optional[str]       # ISO YYYY-MM-DD
    source_citation: Optional[str]  # YAML bio_source


@dataclass(frozen=True)
class SubredditRecord:
    """One `data/major_subreddits.yaml` entry."""
    entity_key: str                 # canonical subreddit name (primary)
    aliases: Tuple[str, ...]        # empty; subreddits have no alias field
    display_name: str
    blurb: str
    raw_lean: Optional[str]         # YAML tilt, pre-flattening
    source_citation: Optional[str]  # YAML tilt_source


@dataclass(frozen=True)
class PoliticalAccountRecord:
    """One handle from `data/known_political_x_accounts.yaml` (the curated
    tier-classification source, distinct from the three editorial
    registries above). Every entry is an elected official or sitting
    executive-branch officeholder by construction of that file."""
    handle: str                     # canonicalized
    display_name: str
    office_title: Optional[str]
    raw_party: Optional[str]        # absent for executive_branch entries


def _build_records(
    path: Path,
    list_key: str,
    *,
    primary_field: str,
    alias_field: Optional[str],
    canonicalizer,
    build_record,
) -> List[Any]:
    """Generic YAML-list loader: parse `list_key` and return one record per
    entry with primary key + aliases canonicalized. Entries missing a usable
    primary key are dropped (mirrors entity_registry.py's `_build_index`)."""
    if not path.exists():
        return []
    with path.open("r", encoding="utf-8") as f:
        data = yaml.safe_load(f) or {}
    records: List[Any] = []
    for raw in data.get(list_key) or []:
        primary = canonicalizer(raw.get(primary_field))
        if not primary:
            continue
        aliases = tuple(
            a for a in (canonicalizer(v) for v in (raw.get(alias_field) or []) if alias_field)
            if a and a != primary
        )
        records.append(build_record(raw, primary, aliases))
    return records


def load_outlets(path: Path) -> List[OutletRecord]:
    """Parse `data/news_outlets.yaml` into `OutletRecord`s."""
    return _build_records(
        path, "outlets",
        primary_field="domain", alias_field="also_domains",
        canonicalizer=canonicalize_news_domain,
        build_record=lambda raw, primary, aliases: OutletRecord(
            entity_key=primary,
            aliases=aliases,
            display_name=raw.get("display_name") or primary,
            blurb=(raw.get("blurb") or "").strip(),
            raw_lean=raw.get("partisan_lean"),
            source_citation=raw.get("lean_source"),
            owner=raw.get("owner"),
        ),
    )


def load_officials(path: Path) -> List[OfficialRecord]:
    """Parse `data/verified_officials.yaml` into `OfficialRecord`s."""
    return _build_records(
        path, "officials",
        primary_field="handle", alias_field="also_handles",
        canonicalizer=canonicalize_handle,
        build_record=lambda raw, primary, aliases: OfficialRecord(
            entity_key=primary,
            aliases=aliases,
            display_name=raw.get("display_name") or primary,
            blurb=(raw.get("blurb") or "").strip(),
            role_title=raw.get("office") or "",
            raw_party=raw.get("party"),
            term_start=str(raw.get("term_start")) if raw.get("term_start") else None,
            source_citation=raw.get("bio_source"),
        ),
    )


def load_subreddits(path: Path) -> List[SubredditRecord]:
    """Parse `data/major_subreddits.yaml` into `SubredditRecord`s."""
    return _build_records(
        path, "subreddits",
        primary_field="subreddit", alias_field=None,
        canonicalizer=canonicalize_subreddit,
        build_record=lambda raw, primary, _aliases: SubredditRecord(
            entity_key=primary,
            aliases=(),
            display_name=raw.get("display_name") or f"r/{primary}",
            blurb=(raw.get("blurb") or "").strip(),
            raw_lean=raw.get("tilt"),
            source_citation=raw.get("tilt_source"),
        ),
    )


def load_political_accounts(path: Path) -> List[PoliticalAccountRecord]:
    """Flatten `data/known_political_x_accounts.yaml`'s nested
    `accounts.executive_branch` + `accounts.congress.{house,senate}` shape
    into one `PoliticalAccountRecord` per handle. Malformed or handle-less
    entries are dropped rather than raising."""
    if not path.exists():
        return []
    with path.open("r", encoding="utf-8") as f:
        data = yaml.safe_load(f) or {}
    accounts = (data.get("accounts") or {})
    records: List[PoliticalAccountRecord] = []
    for person in accounts.get("executive_branch") or []:
        if not isinstance(person, dict):
            continue
        for handle_entry in person.get("accounts") or []:
            handle = canonicalize_handle((handle_entry or {}).get("handle"))
            if not handle:
                continue
            records.append(PoliticalAccountRecord(
                handle=handle,
                display_name=person.get("name") or handle,
                office_title=person.get("office"),
                raw_party=None,
            ))
    congress = accounts.get("congress") or {}
    office_titles = {"house": "Representative", "senate": "Senator"}
    for chamber_key, office_title in office_titles.items():
        for person in congress.get(chamber_key) or []:
            if not isinstance(person, dict):
                continue
            handle = canonicalize_handle(person.get("handle"))
            if not handle:
                continue
            records.append(PoliticalAccountRecord(
                handle=handle,
                display_name=person.get("name") or handle,
                office_title=office_title,
                raw_party=person.get("party"),
            ))
    return records
