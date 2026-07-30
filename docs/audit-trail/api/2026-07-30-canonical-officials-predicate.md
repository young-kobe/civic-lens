# 2026-07-30 — Route officials by entity kind, not the editorial flag

The API query layer now has one definition of "official": `corpus.entities.kind = 'official'`, encoded as `queries/profiles.py::is_official_kind()` and, for tier-only contexts, `queries/constants.py::OFFICIAL_AUTHOR_TIERS = ('elected_official', 'affiliated')`. The `editorial` column is provenance (which YAML seeded the entity) and is no longer read anywhere for routing. This matches the ETL's `official_record` admission (`etl/documents.py`: kind + active, no editorial check), so a doc badged "Official record" can no longer render in a public column.

## What shipped

- `queries/profiles.py`: `is_official_kind()` added; `_entity_ui_kind` returns `'official'` for every official-kind entity (dropped the `editorial` parameter and the account downgrade); `editorial` removed from the profile SQL.
- `queries/sentiment/routing.py`: `_route_own_post` / `_route_received_source` use `is_official_kind`; `_tier_for_row` uses `OFFICIAL_AUTHOR_TIERS` (adds `affiliated` — appointed officials no longer fall to the public tier). `_speaker_tier_4way` keeps its finer officials/affiliated split on purpose: it subdivides the canonical set for received-tone provenance, it does not contradict it.
- `queries/sentiment/panel.py` (`official_ids`, was `editorial_official_ids`), `queries/sentiment/received.py`, `queries/sentiment/sql.py` (dead editorial columns removed), `queries/bots.py`, `queries/propaganda.py` (`_bucket_by_tier` adds `affiliated`), `queries/narratives.py` (already canonical; now imports the shared constant).
- Tests: flipped the non-editorial-official expectations across the four query test modules; `sentiment_panel_basic` / `entity_profile_basic` / `movers_basic` contract snapshots re-recorded (the seeded non-editorial official moves to `byOfficial` with `kind: "official"` and gains its `received` block).

## Why

- Only 16 of ~551 tracked officials are `editorial=true` (`0002_entity_registry_seed.sql`); the other 535 promoted officeholders routed to "The Public" on all four split pages — officials' X posts visibly bled into the public column, labeled "Official record".
- Three near-miss tier definitions (`elected_official` only, editorial-gated kind, `narratives.py`'s two-tier form) could never reconcile; one predicate, stated once, ends the drift.

## Follow-ups

- Officials columns now cover all ~551 officials posting in-window; if stub cards (mentioned-but-silent officials) get noisy, a volume floor is a one-line change — decision surfaced, not pre-built.
