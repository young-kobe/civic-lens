# 2026-07-11 — Outbound targets: WHO public buckets talk about

The Public column's cards (sampled authors, "Other X users") showed what
posts sound like but not who the sentiment is aimed at, even though
`target_mentions` (migration 025) stores resolved targets per doc. UI
entry: `../ui/2026-07-11-modal-clarity-and-targets.md`.

## What shipped

- **`_merge_outbound_targets`** (`aggregators/sentiment.py`): the inverse
  of `received` — the same frozen target_mentions rows grouped by the
  AUTHORING public bucket instead of the mentioned target. Bucket keys
  mirror the final `byGeneralPublic` items (a sampled author consolidated
  below the card floors attributes to "Other X users", matching the card
  the reader sees). Emitted as `EntitySentimentItem.outbound`:
  `{minSampleN, volume, targets: [{label, entityKey, kind, net, volume,
  lowSample}]}`, serialized only when present (stale-cache safe).
  - Resolved targets show their registry display name; party collectives
    a fixed label. Unresolved raw targets earn a named row only when they
    recur (`MIN_OUTBOUND_RAW_RECURRENCE=2`); one-offs and overflow past
    `MAX_OUTBOUND_TARGETS=8` pool into "Other targets" — identity is never
    minted from a single free-text string.
  - Nets share the `MIN_TARGET_SAMPLE_N` suppression floor.
- **Per-sample target chips**: `ClassificationSample.targets`
  (`[{label, stance}]`, resolved first, deduped by label, capped at
  `MAX_TARGETS_PER_SAMPLE=2`). Stamped by a display-only post-pass
  (`_attach_sample_targets`) over every sample surface — byTopic,
  distributionSamples, all three entity tiers — instead of threading a
  parameter through every collector; received-tone samples are skipped
  (on the target's own card the chip would restate the card).
  `entity_posts.py` stamps the same chips per page for the live
  "Show all posts" path.
- `target_sql` now selects `m.raw_target`; both merge passes share the
  same exclusions (bot docs, bot-scored authors, invalid stances,
  SQL-side confidence floor).
- Tests: `OutboundTargetsTests` (`tests/test_target_tone_aggregation.py`)
  — named-author rollup with net math, raw-recurrence rule, one-off
  pooling, consolidated-author attribution to the catch-all, chip order
  and cap, serialization presence rules.

## Why

- Answering "who is this negative sentiment directed at" is the entire
  point of target extraction; before this it only flowed to the TARGET's
  card (received tone). The authoring side now gets the mirror view at
  both scales: the bucket rollup and the per-post chip.
