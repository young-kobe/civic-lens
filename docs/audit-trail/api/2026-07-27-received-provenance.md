# Received-tone provenance: WHO the tone comes from

**Date:** 2026-07-27
**Layer:** api
**Todo:** docs/todos/provenance-and-plain-language.md (retained -- owner eyeball/commit box still open)
**Cross-link:** docs/audit-trail/ui/2026-07-27-plain-language-and-provenance.md

Received tone (how sampled posts talk *about* a tracked official) now
carries provenance: not just the net score, but which kinds of sources
produced it. This completes the sentiment graph's inbound edge -- outbound
("who this bucket talks about", `OutboundTargetCell`) already existed;
received tone previously reported only a number.

## What shipped

- `analysis/src/api/models/sentiment.py`: `ReceivedSourceCell` --
  `source_class` (`news_outlet | official | account | subreddit | x_user |
  other`), `label`, `entity_key`, `lean` (the flat `corpus.political_lean`
  enum, source side -- never `lean_source`), `entity_profile` (registry
  sources only), `share`, `volume`, `net`, `low_sample`. `ReceivedTone`
  gains two lists: `received_from_groups` (rollup by `source_class` x
  `lean` -- every group present, `share` sums to 1.0 across them) and
  `received_from_top` (named individual sources, volume-sorted, capped at
  `MAX_RECEIVED_TOP_SOURCES` = 8).
- `analysis/src/api/queries/sentiment.py`:
  - `_TARGET_ROWS_SQL` widened with `LEFT JOIN corpus.entities e_out` /
    `e_sub` for the outlet's and subreddit's own `lean`, alongside the
    existing `e_auth.lean` (added to the SELECT list).
  - `_route_received_source()` -- the inbound mirror of
    `_route_own_post`/`_route_outbound_bucket`: resolves one mention row
    to `(source_class, ident, lean)`. A registry-resolved news outlet or
    subreddit carries its own lean; an editorial official's post is
    `official`, any other registry account is `account`; an unmatched X
    author is `x_user` keyed by lowercased handle with no lean (per-
    sampled-user derived lean stays out of scope, see the deferred note
    below); everything else -- unmatched outlet/subreddit, unknown
    `source_type`, no author identity at all -- becomes the single
    `other` bucket. This guarantees `received_from_groups`' shares always
    sum to 1.0, even when a slice of the received volume is unattributed.
  - `_accumulate_received()` now also builds `by_source`, a
    `(source_class, ident)` -> stance-counts accumulator, alongside the
    existing topic/speaker-tier/narrative rollups. Applies uniformly to
    both surfaces that flow through it: an editorial official's `received`
    block, and the two party-collective rollups
    (`TargetToneMeta.collectives.gop_collective`/`dem_collective`) --
    collective provenance is built from the same alias-matched-unresolved
    `raw_target` rows that feed the collective's net tone, not from an
    `entity_id`-resolved mention.
  - `_received_source_entity_ids()` widens the id set passed to
    `fetch_entity_profiles()` beyond the tier-bucket ids, so a
    `received_from_top` cell for e.g. an outlet that never posted its own
    documents in the window still carries an `entity_profile`.
  - `_format_received()` emits both new fields via
    `_received_source_groups()` (the rollup, `other` bucket lean forced to
    `None`) and `_received_source_top()` (named sources, `other` excluded
    -- there is no single named source for the pooled bucket).
  - `share` denominator is the received block's own total volume (matches
    `received.volume`); `net`/`low_sample` reuse `MIN_TARGET_SAMPLE_N`,
    same as every other received-tone cell.
- Tests: `analysis/tests/test_api_queries_sentiment.py` --
  `RouteReceivedSourceTests` (pure routing, mirrors
  `RouteOwnPostTests`/`OutboundGroupingTests`) plus
  `test_received_provenance_groups_and_top_sources`, a PG-gated scenario
  seeding one editorial official receiving mentions from a resolved news
  outlet, another editorial official's X post, an unresolved sampled X
  author, and one unattributed mention -- asserts shares sum to 1.0, the
  `other` cell is present with no lean, registry cells carry
  `entity_key`/`entity_profile`, and low-n nets are withheld.
- `analysis/tests/contract/snapshots/sentiment_panel_basic.json`:
  re-recorded -- the two party-collective `received` blocks each gained
  empty `receivedFromGroups`/`receivedFromTop` arrays (the fixture has no
  collective mentions yet); verified byte-identical across two full test
  runs (sha256 `8a96d6a8...`).

## Why

The owner's ask was explicit: received-tone percentages said *what* an
entity receives but not *from whom*. Officials, outlets, and subreddits
already carry a `lean`; the only missing piece was joining it on the
source side of a mention and routing it through the same
tier-bucket-style accumulator pattern the codebase already uses for
outbound targets and own-post buckets, rather than inventing a new shape.

## Follow-ups

- Party-collective provenance (`TargetToneMeta.collectives`) has no UI
  consumer yet -- see docs/audit-trail/ui/2026-07-27-plain-language-and-
  provenance.md and docs/todos/provenance-and-plain-language.md.
- Per-sampled-user derived lean on provenance cells (an `analysis
  .author_leans` join) stays deferred -- revisit if the flat "sampled X
  users" group proves too coarse for readers.

## Follow-up: module split (2026-07-27)

`analysis/src/api/queries/sentiment.py` had grown to ~1,300 lines and is
now `analysis/src/api/queries/sentiment/`, a package with the same public
surface (`get_sentiment_panel`, re-exported from `__init__.py`). Pure
mechanical split, no behavior change: `sql.py` holds the SQL constants and
their thin fetch helpers; `routing.py` holds the row-routing decisions
(`_route_own_post`/`_route_outbound_bucket`/`_route_received_source`/
`_speaker_tier_4way`/`_normalize_party`) plus the small counting/labeling
primitives shared across the other modules; `received.py` holds
received-tone accumulation, provenance, and expressed-alignment
formatting; `outbound.py` holds the outbound-target rollup; `panel.py`
holds the entry point, aggregation, entity-item formatting, own-post tier
bucketing, and response assembly (kept as one file rather than splitting
further into an `entities.py`, since the candidate split would have
required either a real import cycle with `panel.py` or moving shared
primitives out of it -- not worth the churn for a same-behavior
refactor). Verified byte-identical against `sentiment_panel_basic.json`
(sha256 `8a96d6a8...`) across a full gated test run; full suite 892
passed, 0 failed, both gated and ungated.
