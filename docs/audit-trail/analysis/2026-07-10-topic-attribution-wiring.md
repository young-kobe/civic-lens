# 2026-07-10 — byTopic and samples use the LLM topic signal

The Overall Tone page's topic data is now driven by the schema-enforced
LLM topic (`target_mentions.topic`, migration 025) instead of exclusively
by title-keyword matching. Until now two parallel taxonomies shared the
same 14 labels: the LLM's per-target topic enum was persisted and then
discarded by the aggregator, while `byTopic` came from `_extract_topic`
(first keyword hit in the title). UI-layer counterpart:
`docs/audit-trail/ui/2026-07-10-topic-filter-exact.md`.

## What shipped

- `sentiment.py::_fetch_doc_topics`: doc_id → dominant mention topic
  (latest outputs only, per-mention confidence floor, 'Other' excluded so
  an all-'Other' doc falls to the keyword fallback; ties break by count
  then alphabetically). `_aggregate_rows` buckets each doc as
  `doc_topics.get(doc_id) or _extract_topic(title)` — LLM signal wins,
  title keywords are the fallback, "General" stays the honest no-signal
  bucket.
- Every classification sample now carries a `topic` field
  (`_build_sample_dict` + the collectors + `_route_and_record`
  threading). Received-tone samples get their mention's topic; the
  entity-posts drill-down computes the same attribution in SQL (correlated
  dominant-mention subquery with the keyword fallback in Python), so the
  paginated "Show all posts" list filters identically to snapshot samples.
- `aggregator_models.py`: `ClassificationSample.topic`; the three inlined
  sample serializers (entity items, byTopic, distributionSamples) are
  consolidated into `_classification_sample_to_dict` — previously a new
  sample field could reach one surface and silently miss the others.

## Why

- The data-shape survey follow-up: the enum-clean topic signal existed in
  the DB and was never consumed, while the UI re-implemented the keyword
  lists client-side to approximate what the backend already knew.

## Follow-ups

- Entity-scoped-by-topic SCORES still don't exist — the modal filters
  samples exactly but the headline net remains the entity's global score
  (the UI labels this). Now a straightforward GROUP BY over
  target_mentions if wanted.
