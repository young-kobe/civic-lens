"""
Reference-context seeding for LLM prompts.

Loads markdown reference files from ``data/references/`` and matches them
against a doc's text via per-seed keyword lists. Matched seeds are
formatted into a REFERENCE CONTEXT block prepended to the user prompt
of the text-analysis and claim-extraction stages.

Design constraints (from the task brief):

* Sources speak for themselves. Each seed cites an authoritative external
  body (CDC, IPCC, BLS, etc.) with a ``source_url`` and ``last_verified``
  date in its frontmatter. No partisan framing in either the seeds or
  the injected block.
* Tokens are bounded. We cap matched seeds per doc and don't inject the
  block at all when nothing matches, so unrelated docs pay zero cost.
* The seeds are versioned in-tree as markdown so a reviewer can read
  the exact text the model sees alongside the citation, and bumping
  the file's content is a normal commit.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path
from threading import Lock
from typing import List, Optional, Sequence, Tuple

import yaml

# Cap injected seeds so a doc that touches several reference topics doesn't
# inflate the prompt. Three is enough to cover overlapping topics (e.g. a
# climate-and-economy thread) without bloating tokens.
MAX_SEEDS_PER_DOC = 3

_DEFAULT_REFERENCES_DIR = Path(__file__).resolve().parents[3] / "data" / "references"


@dataclass(frozen=True)
class ReferenceSeed:
    """One reference-context entry, parsed from a single markdown file."""
    slug: str                       # filename stem, used as a stable identifier
    topic: str
    source: str
    source_url: str
    last_verified: str              # ISO YYYY-MM-DD
    applies_to_keywords: Tuple[str, ...]
    content: str                    # markdown body

    def matches(self, text_lower: str) -> bool:
        """True if any keyword appears as a whole word in the lowercased text."""
        for kw in self.applies_to_keywords:
            if not kw:
                continue
            # Word-boundary regex avoids "vac" inside "vacuum" matching the
            # vaccines seed, while still catching natural inflections like
            # "vaccinated" if the keyword list includes them.
            pattern = rf"\b{re.escape(kw.lower())}\b"
            if re.search(pattern, text_lower):
                return True
        return False


def _parse_frontmatter(raw: str, slug: str) -> Optional[ReferenceSeed]:
    """Split a `--- ... ---` YAML frontmatter from a markdown body. Returns
    None on parse failure so a single broken file doesn't poison the rest
    of the registry — the loader logs and skips."""
    if not raw.startswith("---"):
        return None
    parts = raw.split("---", 2)
    if len(parts) < 3:
        return None
    _, fm, body = parts
    try:
        meta = yaml.safe_load(fm) or {}
    except yaml.YAMLError:
        return None
    if not isinstance(meta, dict):
        return None
    keywords = meta.get("applies_to_keywords") or []
    if not isinstance(keywords, list):
        return None
    return ReferenceSeed(
        slug=slug,
        topic=str(meta.get("topic", "")).strip(),
        source=str(meta.get("source", "")).strip(),
        source_url=str(meta.get("source_url", "")).strip(),
        last_verified=str(meta.get("last_verified", "")).strip(),
        applies_to_keywords=tuple(str(k).strip().lower() for k in keywords if k),
        content=body.strip(),
    )


_load_lock = Lock()


def load_seeds(directory: Optional[Path] = None) -> List[ReferenceSeed]:
    """Read every ``*.md`` file under ``directory`` and return the parsed
    seeds. Files that don't have valid frontmatter are silently skipped —
    the caller doesn't need a half-loaded registry."""
    root = directory or _DEFAULT_REFERENCES_DIR
    if not root.exists():
        return []
    seeds: List[ReferenceSeed] = []
    for path in sorted(root.glob("*.md")):
        try:
            raw = path.read_text(encoding="utf-8")
        except OSError:
            continue
        seed = _parse_frontmatter(raw, slug=path.stem)
        if seed is not None and seed.applies_to_keywords:
            seeds.append(seed)
    return seeds


@lru_cache(maxsize=1)
def _cached_default_seeds() -> Tuple[ReferenceSeed, ...]:
    """Process-wide cache of the default-directory seeds. Pipelines re-use
    this across thousands of docs; reading + parsing per call would be
    wasted work."""
    with _load_lock:
        return tuple(load_seeds())


def get_seeds() -> Sequence[ReferenceSeed]:
    """Return the cached default-directory seeds. Tests that need a fresh
    read should call ``load_seeds(custom_path)`` directly."""
    return _cached_default_seeds()


def reset_cache() -> None:
    """Clear the module-level seed cache. Used by tests that mutate the
    default references directory mid-process."""
    _cached_default_seeds.cache_clear()


def match_seeds(
    text: str,
    seeds: Optional[Sequence[ReferenceSeed]] = None,
    *,
    max_seeds: int = MAX_SEEDS_PER_DOC,
) -> List[ReferenceSeed]:
    """Return up to ``max_seeds`` seeds whose keyword list matches ``text``.
    Order follows the load order (alphabetical by filename) — stable across
    runs so prompts are deterministic for the same input."""
    if not text:
        return []
    pool = list(seeds if seeds is not None else get_seeds())
    text_lower = text.lower()
    matched: List[ReferenceSeed] = []
    for seed in pool:
        if seed.matches(text_lower):
            matched.append(seed)
            if len(matched) >= max_seeds:
                break
    return matched


def format_seeds_block(seeds: Sequence[ReferenceSeed]) -> str:
    """Render matched seeds as the REFERENCE CONTEXT block to prepend to
    a user prompt. Returns an empty string when ``seeds`` is empty so the
    caller can do a plain string concat without an if-guard."""
    if not seeds:
        return ""
    parts = [
        "REFERENCE CONTEXT (authoritative external sources, neutral framing):",
        "",
    ]
    for seed in seeds:
        parts.append(f"Topic: {seed.topic}")
        parts.append(f"Source: {seed.source}")
        if seed.source_url:
            parts.append(f"Source URL: {seed.source_url}")
        if seed.last_verified:
            parts.append(f"Last verified: {seed.last_verified}")
        parts.append("")
        parts.append(seed.content)
        parts.append("")
        parts.append("---")
        parts.append("")
    parts.append("END REFERENCE CONTEXT.")
    parts.append("")
    return "\n".join(parts)


def matched_slugs(seeds: Sequence[ReferenceSeed]) -> List[str]:
    """Stable list of slugs for the matched seeds. Persisted into
    ai_outputs metadata so the audit trail records which references
    a given inference saw."""
    return [s.slug for s in seeds]
