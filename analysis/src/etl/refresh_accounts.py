"""
Refresh the curated House + Senate sections of known_political_x_accounts.yaml
from the UCSD Library's Congress Twitter guide.

Primary sources (walkthrough 037):
  - Senators: https://ucsd.libguides.com/congress_twitter/senators
  - Reps:     https://ucsd.libguides.com/congress_twitter/reps

Each page exposes a plain HTML <table> with columns (Name "Last, First" | State
| Party). Handles are embedded as <a href="https://x.com/<handle>"> on the name
cell; we extract the handle from the URL, not from link text.

Merge behavior:
  - accounts.congress.house and accounts.congress.senate are rebuilt from the
    scrape.
  - accounts.executive_branch, top-level `affiliated`, top-level `sources`, and
    any other non-congress keys are preserved untouched.
  - counts.{house,senate,total_entries} are updated.
  - When an existing entry matches the scraped handle AND its
    `state_or_district` already contains a district number (e.g. "NC12"), the
    existing value is kept — the scrape only gives us state abbreviations, and
    we don't want to silently throw districts away.
  - Write is atomic (temp file + os.replace) so a crash cannot corrupt the YAML.

Failure mode:
  - If either page is unreachable, log + keep the existing YAML. Never partial-
    write on failure.
"""

from __future__ import annotations

import os
import re
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple
from urllib.parse import urlparse

import requests
import yaml
from bs4 import BeautifulSoup

from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


SENATORS_URL = "https://ucsd.libguides.com/congress_twitter/senators"
REPS_URL = "https://ucsd.libguides.com/congress_twitter/reps"

# A standard browser UA — some gov / edu sites 403 on default requests UA.
USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
)
REQUEST_TIMEOUT_SECONDS = 30


@dataclass
class ScrapedMember:
    """One member row extracted from a UCSD libguide table."""
    name: str               # Display form, first-name-first ("Alma Adams")
    first_name: str
    last_name: str
    chamber: str            # 'house' | 'senate'
    handle: str             # bare handle, no '@'
    url: str                # https://x.com/<handle>
    state: str              # 'NC'
    party: str              # 'D' | 'R' | 'I' | 'L'


@dataclass
class MergeDiff:
    added: List[str] = field(default_factory=list)
    removed: List[str] = field(default_factory=list)
    changed: List[str] = field(default_factory=list)
    preserved_districts: int = 0


# =============================================================================
# Fetch + parse
# =============================================================================

def fetch_html(url: str, session: Optional[requests.Session] = None) -> str:
    """Fetch a URL, returning HTML text. Raises on non-2xx."""
    sess = session or requests.Session()
    response = sess.get(
        url,
        headers={"User-Agent": USER_AGENT, "Accept": "text/html,*/*"},
        timeout=REQUEST_TIMEOUT_SECONDS,
    )
    response.raise_for_status()
    return response.text


def _normalize_name(last_first: str) -> Tuple[str, str, str]:
    """Convert "Adams, Alma" → ("Alma", "Adams", "Alma Adams")."""
    raw = last_first.strip()
    if "," in raw:
        last, first = [s.strip() for s in raw.split(",", 1)]
    else:
        parts = raw.rsplit(" ", 1)
        if len(parts) == 2:
            first, last = parts[0], parts[1]
        else:
            first, last = "", raw
    display = f"{first} {last}".strip()
    return first, last, display


_HANDLE_URL_RE = re.compile(
    r"^https?://(?:www\.)?(?:x|twitter)\.com/([A-Za-z0-9_]+)/?",
    re.IGNORECASE,
)


def _extract_handle(href: str) -> Tuple[Optional[str], Optional[str]]:
    """Return (handle, canonical_url) for a recognized X/Twitter URL."""
    if not href:
        return None, None
    m = _HANDLE_URL_RE.match(href.strip())
    if not m:
        return None, None
    handle = m.group(1)
    return handle, f"https://x.com/{handle}"


def parse_members_html(html: str, chamber: str) -> List[ScrapedMember]:
    """Parse every HTML table on the page and yield one ScrapedMember per row
    whose anchor points at x.com / twitter.com.

    The UCSD pages render one <table> per alphabetical tab (A, B, C, ...) all
    present in the DOM; tab selection is pure CSS, so every table is visible
    to BeautifulSoup.
    """
    if chamber not in ("house", "senate"):
        raise ValueError(f"chamber must be 'house' or 'senate', got {chamber}")

    soup = BeautifulSoup(html, "html.parser")
    members: List[ScrapedMember] = []
    seen_handles: set[str] = set()

    for table in soup.find_all("table"):
        for row in table.find_all("tr"):
            cells = row.find_all("td")
            if len(cells) < 3:
                continue  # header row or layout row

            name_cell, state_cell, party_cell = cells[0], cells[1], cells[2]

            link = name_cell.find("a", href=True)
            if link is None:
                continue
            handle, url = _extract_handle(link["href"])
            if handle is None:
                continue
            if handle.lower() in seen_handles:
                continue
            seen_handles.add(handle.lower())

            name_text = name_cell.get_text(" ", strip=True)
            if not name_text:
                continue
            first, last, display = _normalize_name(name_text)

            state = state_cell.get_text(strip=True).upper()
            party = party_cell.get_text(strip=True).upper()
            # Defensive: state should be 2 letters, party a single letter.
            # Drop rows that don't look like the expected shape rather than
            # importing garbage.
            if not (1 <= len(state) <= 3 and state.isalpha()):
                continue
            if not (len(party) == 1 and party.isalpha()):
                continue

            members.append(ScrapedMember(
                name=display,
                first_name=first,
                last_name=last,
                chamber=chamber,
                handle=handle,
                url=url,
                state=state,
                party=party,
            ))

    return members


def fetch_senators(session: Optional[requests.Session] = None) -> List[ScrapedMember]:
    html = fetch_html(SENATORS_URL, session=session)
    return parse_members_html(html, chamber="senate")


def fetch_representatives(session: Optional[requests.Session] = None) -> List[ScrapedMember]:
    html = fetch_html(REPS_URL, session=session)
    return parse_members_html(html, chamber="house")


# =============================================================================
# YAML merge + atomic write
# =============================================================================

_DISTRICT_RE = re.compile(r"^[A-Z]{2}\d+$")  # "NC12", "CA33" — state + digits


def _member_to_yaml_entry(
    member: ScrapedMember,
    existing_row: Optional[dict],
) -> Dict[str, object]:
    """Produce the YAML payload for one member.

    Three "prefer existing when present" rules reduce churn on refresh:

    1. ``state_or_district``: keep the existing district code (e.g. "NC12")
       when the scraped state matches. The UCSD feed only gives us state
       abbreviations, and clobbering districts would be a downgrade.
    2. ``handle`` casing: X handles are case-insensitive. If the scraped
       handle matches the existing handle case-insensitively, keep the
       existing casing so a one-time switch to UCSD doesn't produce a
       537-row cosmetic diff.
    3. ``url`` tracks the handle casing for the same reason.
    """
    state_or_district: str = member.state
    existing_sd = existing_row.get("state_or_district") if existing_row else None
    if existing_sd and _DISTRICT_RE.match(existing_sd):
        if existing_sd.startswith(member.state):
            state_or_district = existing_sd

    handle = member.handle
    url = member.url
    if existing_row:
        existing_handle_raw = str(existing_row.get("handle") or "").strip().lstrip("@")
        if existing_handle_raw and existing_handle_raw.lower() == member.handle.lower():
            handle = existing_handle_raw
            url = f"https://x.com/{handle}"

    chamber_label = "House" if member.chamber == "house" else "Senate"
    return {
        "name": member.name,
        "first_name": member.first_name,
        "last_name": member.last_name,
        "platform": "x",
        "handle": f"@{handle}",
        "url": url,
        "chamber": chamber_label,
        "state_or_district": state_or_district,
        "party": member.party,
        "source_status": "ucsd_libguide",
    }


def _index_existing_congress(existing: dict, chamber: str) -> Dict[str, dict]:
    """Index existing accounts.congress.<chamber> entries by lowercased handle."""
    path = (existing.get("accounts") or {}).get("congress") or {}
    rows = path.get(chamber) or []
    indexed: Dict[str, dict] = {}
    for row in rows:
        if not isinstance(row, dict):
            continue
        raw = row.get("handle") or ""
        handle = str(raw).strip().lstrip("@").lower()
        if handle:
            indexed[handle] = row
    return indexed


def merge_members_into_yaml(
    existing: dict,
    scraped: List[ScrapedMember],
    chamber: str,
) -> Tuple[dict, MergeDiff]:
    """Return (new_yaml_payload, diff). Does not mutate ``existing`` in place."""
    existing_index = _index_existing_congress(existing, chamber)
    scraped_by_handle: Dict[str, ScrapedMember] = {
        m.handle.lower(): m for m in scraped
    }

    new_rows: List[dict] = []
    diff = MergeDiff()

    # Added + changed
    for handle_lower, member in scraped_by_handle.items():
        existing_row = existing_index.get(handle_lower)
        existing_sd = existing_row.get("state_or_district") if existing_row else None
        entry = _member_to_yaml_entry(member, existing_row=existing_row)
        if existing_row is None:
            diff.added.append(f"@{member.handle} ({member.name})")
        else:
            changed_fields = _field_diff(existing_row, entry)
            if changed_fields:
                diff.changed.append(f"@{member.handle}: {', '.join(changed_fields)}")
        if entry.get("state_or_district") == existing_sd and existing_sd and _DISTRICT_RE.match(existing_sd):
            diff.preserved_districts += 1
        new_rows.append(entry)

    # Removed
    for handle_lower, row in existing_index.items():
        if handle_lower not in scraped_by_handle:
            diff.removed.append(
                f"@{row.get('handle', '').lstrip('@')} ({row.get('name', 'unknown')})"
            )

    # Sort alphabetically by last name to keep diffs readable.
    new_rows.sort(key=lambda r: (r.get("last_name", ""), r.get("first_name", "")))

    # Build the new payload. Deep-copy via YAML roundtrip is overkill — we
    # assign the congress sub-dict directly, preserving every other key.
    new_payload = dict(existing)
    accounts = dict(new_payload.get("accounts") or {})
    congress = dict(accounts.get("congress") or {})
    congress[chamber] = new_rows
    accounts["congress"] = congress
    new_payload["accounts"] = accounts

    # Refresh counts
    counts = dict(new_payload.get("counts") or {})
    counts["house"] = len(congress.get("house") or [])
    counts["senate"] = len(congress.get("senate") or [])
    counts["total_entries"] = (
        counts.get("executive_branch", 0)
        + counts["house"]
        + counts["senate"]
    )
    new_payload["counts"] = counts

    # Touch generated_at_utc
    new_payload["generated_at_utc"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

    return new_payload, diff


_COMPARE_FIELDS = ("name", "state_or_district", "party", "handle")


def _field_diff(existing_row: dict, new_row: dict) -> List[str]:
    """Return the list of field names whose value changed (human-readable).

    Uses plain ASCII arrow (``->``) rather than Unicode U+2192 so that the
    console output renders on Windows PowerShell's default cp1252 codec.
    """
    changed: List[str] = []
    for k in _COMPARE_FIELDS:
        e, n = existing_row.get(k), new_row.get(k)
        if e != n:
            changed.append(f"{k}: {e!r} -> {n!r}")
    return changed


def load_yaml(path: Path) -> dict:
    if not path.exists():
        raise FileNotFoundError(f"Expected existing YAML at {path}")
    with path.open("r", encoding="utf-8") as f:
        data = yaml.safe_load(f) or {}
    return data


def write_atomic_yaml(path: Path, payload: dict) -> None:
    """Write via temp-then-rename so partial writes can't corrupt the file."""
    tmp = path.with_suffix(path.suffix + ".tmp")
    with tmp.open("w", encoding="utf-8") as f:
        yaml.safe_dump(
            payload,
            f,
            sort_keys=False,
            default_flow_style=False,
            allow_unicode=True,
            width=120,
        )
    os.replace(tmp, path)


# =============================================================================
# Orchestration (callable from CLI)
# =============================================================================

@dataclass
class RefreshSummary:
    house: MergeDiff
    senate: MergeDiff
    dry_run: bool
    path: Path


def refresh_accounts(yaml_path: Path, dry_run: bool = False) -> RefreshSummary:
    """End-to-end refresh. Returns a summary; does not raise on partial fetch
    failures beyond network errors (those propagate so the operator knows)."""
    logger.info(f"Loading existing YAML: {yaml_path}")
    existing = load_yaml(yaml_path)

    session = requests.Session()
    logger.info(f"Fetching senators from {SENATORS_URL}")
    senators = fetch_senators(session=session)
    logger.info(f"Fetched {len(senators)} senators")

    logger.info(f"Fetching representatives from {REPS_URL}")
    reps = fetch_representatives(session=session)
    logger.info(f"Fetched {len(reps)} representatives")

    merged, senate_diff = merge_members_into_yaml(existing, senators, chamber="senate")
    merged, house_diff = merge_members_into_yaml(merged, reps, chamber="house")

    logger.info(
        f"Senate: +{len(senate_diff.added)} -{len(senate_diff.removed)} "
        f"~{len(senate_diff.changed)} (preserved {senate_diff.preserved_districts} districts)"
    )
    logger.info(
        f"House:  +{len(house_diff.added)} -{len(house_diff.removed)} "
        f"~{len(house_diff.changed)} (preserved {house_diff.preserved_districts} districts)"
    )

    if dry_run:
        logger.info("Dry run — YAML not written")
    else:
        write_atomic_yaml(yaml_path, merged)
        logger.info(f"Wrote {yaml_path}")

    return RefreshSummary(
        house=house_diff, senate=senate_diff, dry_run=dry_run, path=yaml_path,
    )


def main() -> int:
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--yaml",
        default="data/known_political_x_accounts.yaml",
        help="Path to the YAML to refresh (repo-root relative)",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Fetch, diff, and log — but do not write the YAML.",
    )
    args = parser.parse_args()

    # Repo-root relative → absolute
    path = Path(args.yaml)
    if not path.is_absolute():
        path = Path(os.getcwd()) / path

    try:
        summary = refresh_accounts(path, dry_run=args.dry_run)
    except requests.RequestException as e:
        logger.error(f"Network error while fetching UCSD libguide: {e}")
        logger.error("YAML not modified.")
        return 2
    except FileNotFoundError as e:
        logger.error(str(e))
        return 2

    # Print a short human-readable diff summary for the operator.
    for label, diff in (("Senate", summary.senate), ("House", summary.house)):
        if not (diff.added or diff.removed or diff.changed):
            print(f"{label}: no changes")
            continue
        print(f"\n{label}:")
        for h in diff.added[:20]:
            print(f"  + {h}")
        if len(diff.added) > 20:
            print(f"  + ...and {len(diff.added) - 20} more")
        for h in diff.removed[:20]:
            print(f"  - {h}")
        if len(diff.removed) > 20:
            print(f"  - ...and {len(diff.removed) - 20} more")
        for h in diff.changed[:20]:
            print(f"  ~ {h}")
        if len(diff.changed) > 20:
            print(f"  ~ ...and {len(diff.changed) - 20} more")
        print(f"  (preserved {diff.preserved_districts} house-district codes)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
