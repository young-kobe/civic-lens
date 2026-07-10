"""
Entity registry: news outlets, verified officials, major subreddits.

Loads the three YAML registries from ``data/`` into typed dataclasses
(lazy module singleton), plus the shared routing primitives aggregators
use for the three-way dashboard frame: canonicalizers (strip www./r/@
and lowercase), ``resolve_entity()`` (tier classification), and
catch-all sentinels for unmatched buckets.

Pre-check rationale in walkthrough 055: normalization happens at
registry-match time, not at ingest, because existing rows are already
consistent per-outlet.
"""

from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path
from threading import Lock
from typing import Any, Callable, Dict, Iterable, List, Optional, Tuple, Union

import yaml


_DEFAULT_DATA_DIR = Path(__file__).resolve().parents[3] / "data"
_OUTLETS_FILENAME = "news_outlets.yaml"
_OFFICIALS_FILENAME = "verified_officials.yaml"
_SUBREDDITS_FILENAME = "major_subreddits.yaml"


def _data_dir() -> Path:
    """Registry YAML directory. ``CIVIC_ENTITY_REGISTRY_DIR`` env var
    overrides for tests; read each call so tests can flip between cases."""
    return Path(os.environ.get("CIVIC_ENTITY_REGISTRY_DIR") or _DEFAULT_DATA_DIR)


# --------------------------------------------------------------------------- #
#  Canonicalizers — the contract for "matches the registry".                  #
# --------------------------------------------------------------------------- #

def canonicalize_news_domain(raw: Optional[str]) -> Optional[str]:
    """``www.NYTimes.com`` → ``nytimes.com``."""
    if not raw:
        return None
    d = raw.strip().lower()
    if d.startswith("www."):
        d = d[4:]
    return d or None


def canonicalize_subreddit(raw: Optional[str]) -> Optional[str]:
    """``r/Politics`` / ``/r/Politics`` / ``Politics`` → ``politics``."""
    if not raw:
        return None
    s = raw.strip().lower()
    if s.startswith("/r/"):
        s = s[3:]
    elif s.startswith("r/"):
        s = s[2:]
    return s or None


def canonicalize_handle(raw: Optional[str]) -> Optional[str]:
    """``@POTUS`` → ``potus``. Returns None for empty / bare-``@``."""
    if not raw:
        return None
    h = raw.strip().lower()
    if h.startswith("@"):
        h = h[1:]
    return h or None


# --------------------------------------------------------------------------- #
#  Entity dataclasses                                                         #
# --------------------------------------------------------------------------- #

@dataclass(frozen=True)
class OutletEntity:
    domain: str                 # canonical primary (lowercased, no www.)
    also_domains: tuple
    display_name: str
    blurb: str
    partisan_lean: str          # left | center-left | center | center-right | right | mixed
    lean_source: str
    owner: Optional[str] = None
    founded: Optional[int] = None
    circulation_note: Optional[str] = None

    def profile_dict(self) -> Dict[str, Any]:
        return {
            "kind": "outlet",
            "key": self.domain,
            "displayName": self.display_name,
            "blurb": self.blurb,
            "lean": self.partisan_lean,
            "leanSource": self.lean_source,
            "owner": self.owner,
            "founded": self.founded,
            "circulationNote": self.circulation_note,
        }


@dataclass(frozen=True)
class OfficialEntity:
    handle: str                 # canonical primary (lowercased, no @)
    also_handles: tuple
    display_name: str
    office: str
    party: str
    blurb: str
    term_start: str             # ISO YYYY-MM-DD
    bio_source: str

    def profile_dict(self) -> Dict[str, Any]:
        return {
            "kind": "official",
            "key": self.handle,
            "displayName": self.display_name,
            "blurb": self.blurb,
            "office": self.office,
            "party": self.party,
            "termStart": self.term_start,
            "bioSource": self.bio_source,
        }


@dataclass(frozen=True)
class SubredditEntity:
    subreddit: str              # canonical lowercase, no r/ prefix
    display_name: str           # with r/ prefix
    blurb: str
    tilt: str                   # left | center | right | mixed
    tilt_source: str
    subscriber_count_proxy: Optional[str] = None

    def profile_dict(self) -> Dict[str, Any]:
        return {
            "kind": "subreddit",
            "key": self.subreddit,
            "displayName": self.display_name,
            "blurb": self.blurb,
            "lean": self.tilt,
            "leanSource": self.tilt_source,
            "subscriberCountProxy": self.subscriber_count_proxy,
        }


# --------------------------------------------------------------------------- #
#  Registry loader                                                            #
# --------------------------------------------------------------------------- #

@dataclass
class EntityRegistry:
    """Three index dicts, each keyed by every canonical form (primary +
    aliases) mapping to the same entity instance. Use the module-level
    singleton (``get_registry()``) in production code; construct
    directly only for hermetic tests."""
    outlets: Dict[str, OutletEntity] = field(default_factory=dict)
    officials: Dict[str, OfficialEntity] = field(default_factory=dict)
    subreddits: Dict[str, SubredditEntity] = field(default_factory=dict)

    def get_outlet(self, raw_domain: Optional[str]) -> Optional[OutletEntity]:
        key = canonicalize_news_domain(raw_domain)
        return self.outlets.get(key) if key else None

    def get_official(self, raw_handle: Optional[str]) -> Optional[OfficialEntity]:
        key = canonicalize_handle(raw_handle)
        return self.officials.get(key) if key else None

    def get_subreddit(self, raw_subreddit: Optional[str]) -> Optional[SubredditEntity]:
        key = canonicalize_subreddit(raw_subreddit)
        return self.subreddits.get(key) if key else None

    def primary_outlets(self) -> List[OutletEntity]:
        return _distinct_by_primary(self.outlets.values(), lambda e: e.domain)

    def primary_officials(self) -> List[OfficialEntity]:
        return _distinct_by_primary(self.officials.values(), lambda e: e.handle)

    def primary_subreddits(self) -> List[SubredditEntity]:
        return _distinct_by_primary(self.subreddits.values(), lambda e: e.subreddit)


def _distinct_by_primary(items: Iterable, key_fn: Callable) -> List:
    seen: set = set()
    out: List = []
    for item in items:
        k = key_fn(item)
        if k in seen:
            continue
        seen.add(k)
        out.append(item)
    return out


def _build_index(
    path: Path,
    list_key: str,
    *,
    primary_field: str,
    alias_field: Optional[str],
    canonicalizer: Callable[[Optional[str]], Optional[str]],
    build_entity: Callable[[Dict[str, Any], str, tuple], Any],
) -> Dict[str, Any]:
    """Generic loader: parse a YAML list and return a ``{canonical_key
    → entity}`` map where every alias resolves to the same entity."""
    if not path.exists():
        return {}
    with path.open("r", encoding="utf-8") as f:
        data = yaml.safe_load(f) or {}
    index: Dict[str, Any] = {}
    for raw in data.get(list_key) or []:
        primary = canonicalizer(raw.get(primary_field))
        if not primary:
            continue
        aliases = tuple(
            a for a in (canonicalizer(v) for v in (raw.get(alias_field) or []) if alias_field)
            if a
        )
        entity = build_entity(raw, primary, aliases)
        index[primary] = entity
        for alias in aliases:
            index[alias] = entity
    return index


def load_registry(data_dir: Optional[Path] = None) -> EntityRegistry:
    """Parse all three YAMLs fresh and return a populated registry.
    Prefer the module-level singleton (``get_registry()``) unless you
    need hermetic isolation for a test."""
    root = data_dir or _data_dir()
    return EntityRegistry(
        outlets=_build_index(
            root / _OUTLETS_FILENAME, "outlets",
            primary_field="domain", alias_field="also_domains",
            canonicalizer=canonicalize_news_domain,
            build_entity=lambda raw, primary, aliases: OutletEntity(
                domain=primary,
                also_domains=aliases,
                display_name=raw.get("display_name") or primary,
                blurb=(raw.get("blurb") or "").strip(),
                partisan_lean=raw.get("partisan_lean") or "center",
                lean_source=raw.get("lean_source") or "unsourced",
                owner=raw.get("owner"),
                founded=raw.get("founded"),
                circulation_note=raw.get("circulation_note"),
            ),
        ),
        officials=_build_index(
            root / _OFFICIALS_FILENAME, "officials",
            primary_field="handle", alias_field="also_handles",
            canonicalizer=canonicalize_handle,
            build_entity=lambda raw, primary, aliases: OfficialEntity(
                handle=primary,
                also_handles=aliases,
                display_name=raw.get("display_name") or primary,
                office=raw.get("office") or "",
                party=raw.get("party") or "",
                blurb=(raw.get("blurb") or "").strip(),
                term_start=str(raw.get("term_start") or ""),
                bio_source=raw.get("bio_source") or "",
            ),
        ),
        subreddits=_build_index(
            root / _SUBREDDITS_FILENAME, "subreddits",
            primary_field="subreddit", alias_field=None,
            canonicalizer=canonicalize_subreddit,
            build_entity=lambda raw, primary, _aliases: SubredditEntity(
                subreddit=primary,
                display_name=raw.get("display_name") or f"r/{primary}",
                blurb=(raw.get("blurb") or "").strip(),
                tilt=raw.get("tilt") or "mixed",
                tilt_source=raw.get("tilt_source") or "unsourced",
                subscriber_count_proxy=raw.get("subscriber_count_proxy"),
            ),
        ),
    )


# --------------------------------------------------------------------------- #
#  Module singleton                                                           #
# --------------------------------------------------------------------------- #

_registry_lock = Lock()
_cached_registry: Optional[EntityRegistry] = None


def get_registry() -> EntityRegistry:
    """Return the process-wide singleton, loading YAMLs on first call."""
    global _cached_registry
    with _registry_lock:
        if _cached_registry is None:
            _cached_registry = load_registry()
        return _cached_registry


def reload_registries() -> EntityRegistry:
    """Force the singleton to re-read the YAMLs on next lookup. Used by
    tests that tweak on-disk YAMLs mid-process."""
    global _cached_registry
    with _registry_lock:
        _cached_registry = load_registry()
        return _cached_registry


# --------------------------------------------------------------------------- #
#  Tier classification + catch-all sentinels                                  #
# --------------------------------------------------------------------------- #

Tier = str  # "news" | "officials" | "public"
AnyEntity = Union[OutletEntity, OfficialEntity, SubredditEntity]

CATCH_ALL_OUTLETS = "other-outlets"
CATCH_ALL_X_USERS = "other-x-users"
CATCH_ALL_SUBREDDITS = "other-subreddits"


def catch_all_profile(key: str, display_name: str, blurb: str) -> Dict[str, Any]:
    """Lightweight profile matching ``*Entity.profile_dict()``'s shape
    so the UI can render a card for unmatched buckets. Flagged with
    ``kind='catch_all'``; no partisan-lean fields."""
    return {
        "kind": "catch_all",
        "key": key,
        "displayName": display_name,
        "blurb": blurb,
        "lean": None,
        "leanSource": None,
    }


_ACCOUNT_TIER_DESCRIPTORS = {
    "elected_official": "Elected official or institutional government account",
    "affiliated": "Politically affiliated account",
}


def account_profile_dict(
    handle: str,
    tier: str,
    full_name: Optional[str] = None,
    party: Optional[str] = None,
    office_title: Optional[str] = None,
    account_type: Optional[str] = None,
) -> Dict[str, Any]:
    """Profile for an X account classified by the curated political-accounts
    list (``account_profiles``) but not in the editorial officials registry.
    Shape mirrors ``*Entity.profile_dict()`` with ``kind='account'`` so the
    UI renders a named card instead of folding the author into a catch-all.
    The blurb names the classification source (labeling discipline)."""
    descriptor = office_title or _ACCOUNT_TIER_DESCRIPTORS.get(
        tier, "Classified political account"
    )
    return {
        "kind": "account",
        "key": handle,
        "displayName": full_name or f"@{handle}",
        "blurb": (
            f"{descriptor} on X, classified via the curated political-accounts "
            "list. Not individually tracked in the editorial registry."
        ),
        "lean": None,
        "leanSource": None,
        "party": party,
        "office": office_title or "",
        "accountType": account_type,
    }


# --------------------------------------------------------------------------- #
#  Target-name resolution (received-tone / expressed-tone split)               #
# --------------------------------------------------------------------------- #

# Sentinel keys for collective partisan targets. Party attribution powers the
# speaker-target alignment cells in the sentiment aggregator.
GOP_COLLECTIVE = "gop_collective"
DEM_COLLECTIVE = "dem_collective"

_GOP_TARGET_ALIASES = frozenset({
    "gop", "republican party", "republicans", "republican", "rnc",
    "house republicans", "senate republicans", "congressional republicans",
})
_DEM_TARGET_ALIASES = frozenset({
    "democratic party", "democrats", "democrat", "dems", "dnc",
    "house democrats", "senate democrats", "congressional democrats",
})

# Leading title tokens stripped before lookup ("President Trump" → "trump").
_TARGET_TITLE_PREFIXES = (
    "president", "vice president", "vp", "senator", "sen.", "sen",
    "rep.", "rep", "representative", "speaker", "secretary", "sec.", "sec",
    "leader", "majority leader", "minority leader", "governor", "gov.", "gov",
    "attorney general", "director", "chairman", "chair", "dr.", "dr",
)

# Generational suffixes ignored when deriving a last-name alias
# ("Robert F. Kennedy Jr." → "kennedy").
_NAME_SUFFIXES = frozenset({"jr", "jr.", "sr", "sr.", "ii", "iii", "iv"})


def normalize_target_name(raw: Optional[str]) -> Optional[str]:
    """Canonicalize an LLM-emitted target name for alias lookup:
    lowercase, strip @/possessives/punctuation, drop a leading article and
    leading title tokens."""
    if not raw:
        return None
    s = raw.strip().lower().lstrip("@")
    s = s.rstrip(".,:;!?")
    if s.endswith("'s") or s.endswith("’s"):
        s = s[:-2]
    if s.startswith("the "):
        s = s[4:]
    for prefix in _TARGET_TITLE_PREFIXES:
        if s.startswith(prefix + " "):
            s = s[len(prefix) + 1:]
            break
    s = " ".join(s.split())
    return s or None


class TargetResolver:
    """Resolve raw target names to registry officials or party collectives.

    Alias index per official: canonical handle + also_handles + full display
    name + "<first> <last>" + bare last name (last-name alias dropped when two
    officials share it). Collectives resolve via the static alias sets above.

    ``resolve()`` returns ``(key, kind, party)``:
      * official   → (handle, "official", party)
      * collective → (GOP_COLLECTIVE|DEM_COLLECTIVE, "collective", "R"|"D")
      * no match   → (None, None, None)
    """

    def __init__(self, registry: EntityRegistry):
        self._index: Dict[str, Tuple[str, str]] = {}   # alias → (handle, party)
        last_name_owners: Dict[str, set] = {}
        for official in registry.primary_officials():
            aliases = {official.handle, *official.also_handles}
            display = normalize_target_name(official.display_name)
            if display:
                aliases.add(display)
                tokens = [t for t in display.split() if t not in _NAME_SUFFIXES]
                if len(tokens) >= 2:
                    aliases.add(f"{tokens[0]} {tokens[-1]}")
                    last_name_owners.setdefault(tokens[-1], set()).add(official.handle)
            for alias in aliases:
                self._index[alias] = (official.handle, official.party)
        # Bare last names are only safe when unambiguous.
        for last, owners in last_name_owners.items():
            if len(owners) == 1:
                handle = next(iter(owners))
                self._index.setdefault(last, (handle, registry.officials[handle].party))

    def resolve(self, raw_name: Optional[str]) -> Tuple[Optional[str], Optional[str], Optional[str]]:
        key = normalize_target_name(raw_name)
        if not key:
            return None, None, None
        if key in _GOP_TARGET_ALIASES:
            return GOP_COLLECTIVE, "collective", "R"
        if key in _DEM_TARGET_ALIASES:
            return DEM_COLLECTIVE, "collective", "D"
        hit = self._index.get(key)
        if hit is not None:
            return hit[0], "official", hit[1]
        return None, None, None


def resolve_entity(
    registry: EntityRegistry,
    source_type: Optional[str],
    domain_or_subreddit: Optional[str],
    x_handle: Optional[str],
    is_official_tier: bool = False,
) -> Tuple[Optional[Tier], Optional[AnyEntity]]:
    """Classify a doc into (tier, matched_entity).

    * news → ("news", OutletEntity | None)
    * x_post authored by a verified official → ("officials", OfficialEntity)
    * x_post with ``is_official_tier`` provenance but no registry match →
      ("officials", None)
    * x_post by anyone else → ("public", None)
    * reddit → ("public", SubredditEntity | None)
    * unknown source_type → (None, None)

    ``is_official_tier`` is the ingestor's provenance flag
    (``x_posts_raw.is_official_tier``): the post was pulled via the
    verified-officials timeline. It upgrades a post to the officials tier even
    when the stored handle string doesn't canonicalize to a registry key
    (renamed handle, alt handle not in the editorial list) — a robustness
    signal independent of string matching. Callers that pass it must tolerate
    an ("officials", None) return (no editorial entity to render).
    """
    if source_type == "news":
        return "news", registry.get_outlet(domain_or_subreddit)
    if source_type == "x_post":
        official = registry.get_official(x_handle)
        if official is not None:
            return "officials", official
        if is_official_tier:
            return "officials", None
        return "public", None
    if source_type in ("reddit_post", "reddit_comment"):
        return "public", registry.get_subreddit(domain_or_subreddit)
    return None, None
