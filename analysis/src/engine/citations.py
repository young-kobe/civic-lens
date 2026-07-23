"""
Deterministic cross-source citation extractor (Postgres redesign Phase 6).

Produces a *partial* link graph, not a full propagation trace (see CLAUDE.md,
walkthrough 035): URL citations found in doc text, plus X reply/quote/retweet
edges from `referenced_tweet_id`/`referenced_tweet_type`. `extract()` is pure
(no DB); `process()` resolves candidates against `corpus.documents` and writes
one `analysis.citations` run via `results/store.py`.

Caller contract: `referenced_tweet_id`/`referenced_tweet_type` are
snapshotted onto `corpus.x_posts` at ETL load time (analysis/src/etl/
documents.py, same as the engagement counts) — the caller reads them off
`corpus.x_posts` to populate `CitationDocInput` for x_post docs; no
analysis-time join against `raw.*` is needed.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Optional

from analysis.src.common import db
from analysis.src.common.canonicalize import canonicalize_url
from analysis.src.results import store

# =============================================================================
# Citations-only constants. Single-use to this module -- NOT added to
# engine/constants.py because a sibling agent is concurrently appending to
# that file for another engine; move here if/when something below turns out
# to be genuinely shared (none of it is expected to be).
# =============================================================================

MODEL_ID = "citation_extractor_v1"

# Deterministic extraction carries no uncertainty -- the old convention this
# ports forward (see the old engine/citation_extractor.py docstring: no
# ai_outputs confidence was ever computed for this stage; the new run row
# still needs a confidence value, and 1.0 is the correct "not probabilistic"
# reading for a rule-based match).
DETERMINISTIC_CONFIDENCE = 1.0

# Loose URL matcher -- canonicalize_url() is what actually decides matches.
_URL_REGEX = re.compile(r'https?://[^\s<>"\'\)\]]+', re.IGNORECASE)

# Map X API's referenced_tweet_type -> analysis.link_type.
_X_REFERENCE_LINK_TYPE = {
    "quoted": "quote",
    "replied_to": "reply",
    "retweeted": "retweet",
}


@dataclass(frozen=True)
class CitationDocInput:
    """One doc's citation-relevant fields, assembled by the caller from
    corpus.documents (+ raw.x_posts for the two x-specific fields)."""

    doc_id: int
    source_type: str  # corpus.source_type: 'news' | 'reddit_post' | 'x_post'
    text: str
    referenced_tweet_id: Optional[str] = None
    referenced_tweet_type: Optional[str] = None


@dataclass(frozen=True)
class CitationCandidate:
    """One extracted edge before target resolution. Exactly one of
    target_url/target_tweet_id is set -- whichever key process() resolves
    against corpus.documents.natural_key to decide target_doc_id vs
    target_url."""

    link_type: str  # analysis.link_type: 'url_citation' | 'quote' | 'reply' | 'retweet'
    target_url: Optional[str] = None
    target_tweet_id: Optional[str] = None


def extract(doc: CitationDocInput) -> list[CitationCandidate]:
    """Pure extraction: text URL citations (deduped by canonical form) plus,
    for x_post docs, one reply/quote/retweet candidate keyed by the
    referenced tweet's natural id. No DB access."""
    candidates: list[CitationCandidate] = []

    if doc.source_type == "x_post" and doc.referenced_tweet_id and doc.referenced_tweet_type:
        link_type = _X_REFERENCE_LINK_TYPE.get(doc.referenced_tweet_type)
        if link_type:
            candidates.append(
                CitationCandidate(link_type=link_type, target_tweet_id=doc.referenced_tweet_id)
            )

    seen_urls: set[str] = set()
    for raw_url in _URL_REGEX.findall(doc.text or ""):
        canon = canonicalize_url(raw_url)
        if canon in seen_urls:
            continue
        seen_urls.add(canon)
        candidates.append(CitationCandidate(link_type="url_citation", target_url=canon))

    return candidates


def _candidate_key(candidate: CitationCandidate) -> Optional[str]:
    return candidate.target_url if candidate.target_url is not None else candidate.target_tweet_id


def resolve_candidates(
    source_doc_id: int,
    candidates: list[CitationCandidate],
    resolved: dict[str, int],
) -> list[store.CitationRow]:
    """Turn extracted candidates into store rows given a natural_key ->
    doc_id lookup already fetched for this doc's candidates.

    - A candidate whose key resolves to source_doc_id itself is a
      self-citation and is dropped entirely.
    - A resolved url_citation or x-reference becomes target_doc_id.
    - An unresolved url_citation falls back to target_url (we know the URL).
    - An unresolved x-reference is dropped: we don't know the referenced
      tweet's URL without a re-fetch, so a half-useful edge is worse than no
      edge (ported unchanged from the old extractor's rationale).
    """
    rows: list[store.CitationRow] = []
    for candidate in candidates:
        target_doc_id = resolved.get(_candidate_key(candidate))
        if target_doc_id == source_doc_id:
            continue
        if target_doc_id is not None:
            rows.append(store.CitationRow(link_type=candidate.link_type, target_doc_id=target_doc_id))
        elif candidate.link_type == "url_citation":
            rows.append(store.CitationRow(link_type=candidate.link_type, target_url=candidate.target_url))
    return rows


def _resolve_natural_keys(keys: set[str]) -> dict[str, int]:
    """One batched lookup for every candidate key of a doc (not per-candidate).
    Matches corpus.documents.natural_key regardless of source_type -- mirrors
    the old extractor's single generic `docs.ident` lookup."""
    if not keys:
        return {}
    with db.connection() as conn:
        rows = conn.execute(
            "SELECT natural_key, doc_id FROM corpus.documents WHERE natural_key = ANY(%s)",
            (list(keys),),
        ).fetchall()
    return {row["natural_key"]: row["doc_id"] for row in rows}


def process(doc: CitationDocInput) -> store.RunOutcome:
    """Extract, resolve, and persist one deterministic citations run for
    `doc`. Zero candidates (most docs cite nothing) is a legitimate outcome:
    a 'done' run with no analysis.citations rows. Returns the RunOutcome
    from store.RunHandle.finish()."""
    candidates = extract(doc)
    keys = {_candidate_key(c) for c in candidates if _candidate_key(c) is not None}
    resolved = _resolve_natural_keys(keys)
    rows = resolve_candidates(doc.doc_id, candidates, resolved)

    handle = store.open_run(
        "citations",
        doc_id=doc.doc_id,
        model_id=MODEL_ID,
        prompt_version=None,
        inference_method="deterministic",
    )
    handle.save_citations(rows)
    return handle.finish("done", confidence=DETERMINISTIC_CONFIDENCE)
