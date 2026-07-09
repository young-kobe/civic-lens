"""
Registry-consistency contract test (audit D-1b, D-7, D-8).

Turns three MUST-comments spread across the editorial YAMLs into checks CI
runs for free:

  1. Every verified_officials handle (primary + also_handles) exists in
     known_political_x_accounts.yaml — the contract that file's own header
     states ("Handles MUST also exist in ...").
  2. No duplicate handles inside known_political_x_accounts.yaml — the curated
     loader upserts one row per handle, so a duplicate silently last-writes.
  3. Every account_type is within migration 011's documented enum — there is
     no CHECK constraint on the column to catch an off-enum value.
  4. No null handle without an explicit source_status note — a null handle is
     silently skipped by the loader, so it must be annotated deliberately.
  5. Seeds vs registries cover each other in both directions (news outlets +
     subreddits), with explicit EXCEPTIONS sets for deliberate editorial
     divergences (registered-but-unseeded outlets/subreddits we keep on the
     dashboard even though no feed pulls them).

Uses PyYAML directly plus the real curated-list parser so the checks track the
same code the pipeline runs.
"""

import sys
import unittest
from collections import Counter
from pathlib import Path
from urllib.parse import urlparse

import yaml

_repo_root = Path(__file__).resolve().parents[2]
if str(_repo_root) not in sys.path:
    sys.path.insert(0, str(_repo_root))

from analysis.src.engine.account_classifier import _parse_curated_yaml, _strip_handle

_DATA = _repo_root / "data"


def _norm_handle(raw) -> str:
    return _strip_handle(raw).lower()


def _load(name: str) -> dict:
    with (_DATA / name).open("r", encoding="utf-8") as f:
        return yaml.safe_load(f) or {}


# account_type enum documented in data/migrations/011_account_profiles_faction.sql.
ACCOUNT_TYPE_ENUM = {
    "official", "official_role", "personal", "personal/political",
    "personal/official", "institutional", "previous/personal",
}

# --- D-8 deliberate seed/registry divergences ------------------------------- #
# Outlets kept on the dashboard as first-class entities even though no RSS seed
# pulls them directly (editorial reach outweighs a missing feed).
UNSEEDED_OUTLET_EXCEPTIONS = {
    "usatoday.com", "bloomberg.com", "theatlantic.com", "nationalreview.com",
}
# Seed feed domains that intentionally do not become their own registry entry.
SEED_OUTLET_EXCEPTIONS: set = set()
# Subreddits registered as first-class entities but not currently fetched.
UNSEEDED_SUBREDDIT_EXCEPTIONS = {"republican", "libertarian", "neoliberal"}
SEED_SUBREDDIT_EXCEPTIONS: set = set()

# Proxy/feed hostnames whose article outlet differs from the feed host. Keyed by
# a substring of the seed URL so the mapping is unambiguous.
_SEED_URL_ALIASES = {
    "feeds.a.dj.com": "wsj.com",       # Dow Jones opinion feed = WSJ
    "feeds.nbcnews.com": "msnbc.com",  # this feed path is /msnbc/public/politics
    "feedburner.com/breitbart": "breitbart.com",
    "bbci.co.uk": "bbc.com",
}


def _seed_outlet_domain(url: str) -> str:
    """Map an RSS seed URL to the outlet domain the registry keys on."""
    for frag, dom in _SEED_URL_ALIASES.items():
        if frag in url:
            return dom
    host = (urlparse(url).hostname or "").lower()
    parts = host.split(".")
    return ".".join(parts[-2:]) if len(parts) >= 2 else host


class RegistryConsistencyTests(unittest.TestCase):

    @classmethod
    def setUpClass(cls):
        cls.officials = _load("verified_officials.yaml")
        cls.known = _load("known_political_x_accounts.yaml")
        cls.outlets = _load("news_outlets.yaml")
        cls.subreddits = _load("major_subreddits.yaml")
        cls.seeds = _load("seeds.yaml")
        cls.known_entries = _parse_curated_yaml(cls.known)
        cls.known_handles = {_norm_handle(e.handle) for e in cls.known_entries}

    # 1 -------------------------------------------------------------------- #
    def test_every_official_handle_is_in_known_accounts(self):
        missing = []
        for off in self.officials.get("officials") or []:
            for h in [off.get("handle")] + list(off.get("also_handles") or []):
                if _norm_handle(h) and _norm_handle(h) not in self.known_handles:
                    missing.append(h)
        self.assertEqual(
            missing, [],
            "verified_officials handles absent from known_political_x_accounts.yaml "
            "(violates that file's MUST-contract): " + ", ".join(missing),
        )

    # 2 -------------------------------------------------------------------- #
    def test_no_duplicate_handles_in_known_accounts(self):
        counts = Counter(_norm_handle(e.handle) for e in self.known_entries)
        dups = sorted(h for h, c in counts.items() if c > 1)
        self.assertEqual(
            dups, [],
            "duplicate handles in known_political_x_accounts.yaml (curated loader "
            "would last-write-wins these): " + ", ".join(dups),
        )

    # 3 -------------------------------------------------------------------- #
    def test_account_types_within_enum(self):
        bad = []
        for person in (self.known.get("accounts") or {}).get("executive_branch") or []:
            for acc in person.get("accounts") or []:
                at = acc.get("account_type")
                if at is not None and at not in ACCOUNT_TYPE_ENUM:
                    bad.append(f"{acc.get('handle')}={at}")
        self.assertEqual(
            bad, [],
            "off-enum account_type values (migration 011 documents the enum; no "
            "CHECK enforces it): " + ", ".join(bad),
        )

    # 4 -------------------------------------------------------------------- #
    def test_null_handles_are_annotated(self):
        congress = (self.known.get("accounts") or {}).get("congress") or {}
        unannotated = []
        for chamber in ("house", "senate"):
            for person in congress.get(chamber) or []:
                if not _strip_handle(person.get("handle")) and "source_status" not in person:
                    unannotated.append(person.get("name") or "<unnamed>")
        self.assertEqual(
            unannotated, [],
            "null-handle members without a source_status note (silently skipped "
            "by the loader): " + ", ".join(unannotated),
        )

    # 5 -------------------------------------------------------------------- #
    def _registry_outlet_domains(self) -> set:
        domains = set()
        for o in self.outlets.get("outlets") or []:
            domains.add((o.get("domain") or "").lower())
            for alt in o.get("also_domains") or []:
                domains.add((alt or "").lower())
        return {d for d in domains if d}

    def test_seed_outlets_are_registered(self):
        registered = self._registry_outlet_domains()
        unregistered = []
        for seed in self.seeds.get("seeds") or []:
            if seed.get("type") != "rss":
                continue
            dom = _seed_outlet_domain(seed.get("url") or "")
            if dom and dom not in registered and dom not in SEED_OUTLET_EXCEPTIONS:
                unregistered.append(dom)
        self.assertEqual(
            sorted(set(unregistered)), [],
            "RSS-seeded outlets missing from news_outlets.yaml (docs land "
            "unlabeled): " + ", ".join(sorted(set(unregistered))),
        )

    def test_registered_outlets_are_seeded_or_excepted(self):
        seeded = {
            _seed_outlet_domain(s.get("url") or "")
            for s in (self.seeds.get("seeds") or [])
            if s.get("type") == "rss"
        }
        orphaned = []
        for o in self.outlets.get("outlets") or []:
            dom = (o.get("domain") or "").lower()
            alts = {(a or "").lower() for a in (o.get("also_domains") or [])}
            if dom and dom not in seeded and not (alts & seeded) \
                    and dom not in UNSEEDED_OUTLET_EXCEPTIONS:
                orphaned.append(dom)
        self.assertEqual(
            sorted(orphaned), [],
            "registered outlets neither seeded nor in UNSEEDED_OUTLET_EXCEPTIONS: "
            + ", ".join(sorted(orphaned)),
        )

    def test_seed_subreddits_are_registered(self):
        registered = {
            (s.get("subreddit") or "").lower()
            for s in (self.subreddits.get("subreddits") or [])
        }
        seeded = [(x or "").lower() for x in (self.seeds.get("reddit") or {}).get("subreddits") or []]
        unregistered = [
            s for s in seeded
            if s and s not in registered and s not in SEED_SUBREDDIT_EXCEPTIONS
        ]
        self.assertEqual(
            sorted(set(unregistered)), [],
            "seeded subreddits missing from major_subreddits.yaml: "
            + ", ".join(sorted(set(unregistered))),
        )

    def test_registered_subreddits_are_seeded_or_excepted(self):
        seeded = {(x or "").lower() for x in (self.seeds.get("reddit") or {}).get("subreddits") or []}
        orphaned = []
        for s in self.subreddits.get("subreddits") or []:
            name = (s.get("subreddit") or "").lower()
            if name and name not in seeded and name not in UNSEEDED_SUBREDDIT_EXCEPTIONS:
                orphaned.append(name)
        self.assertEqual(
            sorted(orphaned), [],
            "registered subreddits neither fetched nor in "
            "UNSEEDED_SUBREDDIT_EXCEPTIONS: " + ", ".join(sorted(orphaned)),
        )


if __name__ == "__main__":
    unittest.main()
