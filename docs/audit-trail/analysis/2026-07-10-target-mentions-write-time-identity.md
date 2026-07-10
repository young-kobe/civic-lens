# 2026-07-10 — Persist target-entity identity at write time (target_mentions)

Extracted target stances now live in a normalized `target_mentions` table
(migration 025), one row per (doc, target), with the entity-registry
resolution performed ONCE at extraction time and frozen. Previously the
target list existed only inside `ai_outputs.output_json` and was re-resolved
from free text on every snapshot aggregation: unresolved mentions vanished
(only a counter survived), and a registry edit silently remapped history —
the same doc could count toward different officials week to week.

## What shipped

- `data/migrations/025_target_mentions.sql`: `target_mentions`
  (`output_id`, `doc_id`, `raw_target`, `entity_key`/`entity_kind`/
  `entity_party`, `stance` CHECK, `topic`, `confidence`, `evidence_json`),
  FKs to `ai_outputs`/`docs`, indexes on output/doc/entity. `raw_target`
  preserves the LLM's original string for audit; unresolved mentions
  persist with `entity_key` NULL instead of disappearing.
- `etl/loader.py`: `save_ai_output` returns the new `output_id`;
  `build_target_mentions` (shapes + resolves; takes a resolve callable so
  etl never imports the reporting layer), `save_target_mentions`, and
  `backfill_target_mentions` — a deterministic, idempotent materializer
  (anti-join on existing mentions) for the pre-025 corpus and any
  reprocessed rows.
- `scheduler/job_runner.py::run_target_extraction`: builds a
  `TargetResolver` per run, writes mention rows inline with each
  extraction, and runs the backfill once per stage run (so the existing
  corpus materializes on the first `analyze` after upgrade).
- Read path (`reporting/aggregators/sentiment.py`): the received-tone /
  alignment merge consumes `target_mentions` joined through
  `ai_outputs_latest` (a reprocessed doc's superseded mentions drop out
  automatically). The per-mention confidence floor moved into SQL; the
  read-time `TargetResolver` construction and JSON fan-out are deleted.
  `output_json` rides along only for the doc-level reasoning shown in
  samples; per-doc context (speaker resolution, engagement weight,
  narratives) is cached per doc_id across that doc's mentions.

## Behavior notes

- Unresolved-mention counts now come from persisted NULL-entity rows —
  same numbers, but auditable after the fact (`SELECT raw_target ...
  WHERE entity_key IS NULL` shows exactly WHAT failed to resolve).
- `botExcludedMentions` now counts only mentions above the confidence
  floor (the SQL filter runs first); previously it counted every
  well-formed target on the excluded doc regardless of confidence.
- Re-resolving history under a changed registry is a deliberate
  maintenance action: DELETE the affected mention rows and run the targets
  stage — the backfill re-materializes from the JSON audit payload with
  the current registry. It is never automatic.

## Why

- Data-shape survey finding 6 (the largest): read-time resolution made
  target identity unstable and unauditable, and it ran per snapshot per
  window. Write-time resolution makes the mention the durable unit of the
  received-tone product.

## Follow-ups

- The doc-level `byTopic` taxonomy still comes from title-keyword matching
  (`_extract_topic`), not from the schema-enforced `target_mentions.topic`
  the aggregator now has available in SQL — candidate next initiative.
