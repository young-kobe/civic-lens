# 2026-07-09 — Data-contract remediation (D-1..D-13)

Closes the confirmed findings from `2026-07-09-adversarial-review-data-layer.md`.
The data layer is the contract between the Go ingestor and the Python analysis
code; this pass repairs the broken halves of that contract and adds a
CI-runnable check that keeps the registry side honest. The system stays on
SQLite (a Postgres migration was considered and deferred — every fix here,
including the schema DDL, targets SQLite directly). D-3 and D-9 landed on the
ingestion-review branch and are not repeated here.

## What shipped

### Registry hygiene + enforcement (D-1, D-7, D-8, D-1b)
- All 12 verified_officials handles missing from
  `known_political_x_accounts.yaml` now resolve there: the four congressional
  leaders (Johnson/Thune/Schumer/Jeffries) via a new `also_handles` list on
  their congress rows (the curated loader `account_classifier.py` now emits one
  `account_profiles` row per handle); the RNC/DNC chairs (`@ChairmanWhatley`,
  `@kenmartinmn`) via the top-level `affiliated` block; and the Noem/Bondi/RFK
  cabinet handles via executive_branch entries. Both tier systems
  (`entity_registry` + `account_profiles`) now agree on the prominent officials.
- **D-7a**: the DHS seat is reconciled to reality — `@DHSgov` belongs to Kristi
  Noem's executive entry; Markwayne Mullin is only the OK senator (`@SenMullin`,
  senate). The duplicate/incorrect Mullin-as-DHS row is gone.
- **D-7b**: Rep. Christopher Smith's `handle: null` is filled (`@RepChrisSmith`,
  annotated `source_status: manual_fill`); the two off-enum
  `account_type: official/personal` values are corrected to `personal/official`.
- **D-8**: `cbsnews.com` (a priority-10 RSS seed) added to `news_outlets.yaml`;
  the five seeded-but-unregistered subreddits (news, worldnews, NeutralPolitics,
  moderatepolitics, geopolitics) added to `major_subreddits.yaml`. The four
  registered-but-unseeded outlets (bloomberg, nationalreview, theatlantic,
  usatoday) and three unfetched subreddits (republican, libertarian, neoliberal)
  are **kept** as deliberate editorial entities and recorded in explicit
  EXCEPTIONS sets in the new test — deleting curated editorial rows to satisfy a
  seed list was the wrong trade.
- **D-1b**: `analysis/tests/test_registry_consistency.py` turns three
  MUST-comments into checks: every officials handle exists in known-accounts,
  no duplicate handles, account_type within migration 011's enum, no null handle
  without a `source_status` note, and seeds<->registry coverage both ways with
  the EXCEPTIONS above.

### Cross-language guarantees (D-5, D-6, D-12)
- **D-5**: `PRAGMA foreign_keys=ON` now runs on every `sqlite3.connect` in
  `analysis/src` (loader, health, account_classifier, citation_extractor,
  narrative_clusterer, aggregators/base). Python now enforces the same FKs the
  Go DSN already did. Full suite re-run surfaced no orphan-writing bugs.
- **D-6**: `db.go` wraps each migration file + its version INSERT in one
  transaction, so a crash/failed statement mid-migration rolls back and a re-run
  succeeds (`TestMigrateAtomicRollback`). Migrations that manage their own
  transaction (the `BEGIN...COMMIT` table rebuilds, which also toggle
  `PRAGMA foreign_keys` — a no-op inside a tx) are detected and run as-is; the
  manual `BEGIN/COMMIT` was stripped from 017/018 so the wrapper owns them.
- **D-12**: migration 021 backfills migration 004's missing `schema_version`
  row (`INSERT OR IGNORE ... VALUES (4, ...)`), guarded so migrated DBs are
  untouched. A scratch DB now reports contiguous versions 1-21.

### Schema truth (D-2, D-10, D-13)
- **D-10 / D-13**: migration 021 drops `docs.place_country_code` (+
  `idx_docs_country`) and `docs.fetched_at` — both were write-only/dead. The
  loader no longer writes the docs column; `place_country_code` stays inside
  `metadata_json`, where `bot.py`'s foreign-origin flag actually reads it.
- **D-2**: `docs/DATABASE_SCHEMA.md` regenerated from a scratch DB built off
  migrations 001-021 — all 16 live tables, columns, FKs, indexes, the `pages`
  state CHECK, and `source_type` including `x_post`. The three dropped-table
  sections and the `file:///c:/Users/...` link are gone.

### is_official_tier gets a reader (D-4)
- `resolve_entity` takes an optional `is_official_tier`; the sentiment
  aggregator selects `x_posts_raw.is_official_tier` and routes a flagged post to
  the officials tier even when its stored handle doesn't match the editorial
  registry, bucketing handle-unmatched provenance posts into a dedicated
  verified-officials sentinel. Covered by `test_official_tier_routing.py`. The
  column is now both written and read.

### Comment/code drift (D-11)
- `seeds.yaml` ("$30 cap" → the real $50; "~24 handles" → the 37 AllHandles()
  returns) and `config.go` ("roughly 16 handles" → 37) corrected.

## Why

The registries encode a MUST-contract ("handles MUST also exist in ...") that
was only a comment, so 12 of 37 official handles silently disagreed between the
two tier systems; FK enforcement existed on the Go side only; a crash inside a
bare-ALTER migration wedged every later `migrate`; and the schema doc was frozen
at migration 001 and actively wrong. Each fix either repairs a contract half or
adds the check that keeps it from rotting again.

## Follow-ups

- The `postingCadence`/heatmap payload drop, the `/bot-activity` window param,
  honest sentiment coverage/confidence, and narrative mean-confidence (deferred
  Python backend follow-ups from `docs/todos/ui-rework.md`) landed alongside
  this work; see `../ui/2026-07-09-ui-remediation.md`.
- UI `types.ts`/`fixtures.ts` still reference `postingCadence`; reconcile at the
  branch-41 merge (UI half already dropped the viz).
