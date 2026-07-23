# Civic Lens Database Schema Reference

> **TARGET SCHEMA — not yet live in production.** The live system runs the
> v2.0 SQLite schema (below the fold, unchanged) until cutover. Tracking:
> `docs/todos/pg-redesign.md`. Do not point application code at this schema
> until the corresponding phase has landed and the todo box is checked.

> **Version**: 3.0 (target)
> **Last updated**: 2026-07-22
> **Source of truth**: `data/pg-migrations/0001_north_star.sql` — this
> document is regenerated from that file; when it drifts, rebuild and
> re-diff rather than hand-editing.

---

## Overview

Six namespaced Postgres 17 schemas replace the single flat SQLite database.
Surrogate keys are `BIGINT GENERATED ALWAYS AS IDENTITY` everywhere except
`raw.*` (natural keys — url_canon/fullname/tweet_id/user_id, kept
byte-faithful to the crawler) and `archive.*` (imported SQLite integer IDs,
preserved verbatim). All timestamps are `TIMESTAMPTZ`. JSONB appears in
exactly three places: `analysis.runs.raw_response`,
`ops.pipeline_runs.stage_summary`, and `archive.*` (verbatim import of old
JSON columns).

| Schema | Writer | Purpose |
| --- | --- | --- |
| `raw` | Go ingestor only | Frontier + verbatim source capture |
| `corpus` | Python ETL only | Normalized documents, authors, entity registry |
| `analysis` | `results/store.py` + named aggregate/derived modules | Analysis runs + typed per-task results |
| `serving` | serving/ rollup builders | Precomputed rollups the API reads |
| `ops` | Go + Python scheduler | Work queue, run provenance, budget, migrations |
| `archive` | one-time import script | Verbatim old SQLite derived data, read-only by convention |

---

## `raw` — capture layer

Near-1:1 port of the old ingestion tables — deliberately not redesigned;
rows must survive migration byte-faithfully.

### raw.pages (frontier)

`state`: `raw.page_state` enum (`queued`/`inflight`/`done`/`failed`).
`inflight` rows reset to `queued` on startup.

| Column | Type | Notes |
| --- | --- | --- |
| url_canon | TEXT | PK |
| url_raw | TEXT | NOT NULL |
| domain | TEXT | NOT NULL |
| state | raw.page_state | NOT NULL DEFAULT 'queued' |
| priority | INTEGER | NOT NULL DEFAULT 0 |
| retries | INTEGER | NOT NULL DEFAULT 0 |
| next_fetch_at / inflight_at | TIMESTAMPTZ | NOT NULL DEFAULT epoch |
| http_status | INTEGER | |
| content_sha256, etag, last_modified, last_error | TEXT | |

Indexes: `(state, next_fetch_at)`, `(domain)`.

### raw.articles

| Column | Type | Notes |
| --- | --- | --- |
| url_canon | TEXT | PK, FK -> raw.pages |
| domain | TEXT | |
| fetched_at | TIMESTAMPTZ | NOT NULL |
| published_at | TIMESTAMPTZ | |
| title | TEXT | |
| raw_hash | TEXT | NOT NULL — key into `data/raw/sha256/` |
| extraction_version | TEXT | NOT NULL |

### raw.reddit_posts

| Column | Type | Notes |
| --- | --- | --- |
| fullname | TEXT | PK |
| subreddit | TEXT | |
| created_utc, fetched_at | TIMESTAMPTZ | |
| title, body | TEXT | |
| score, num_comments | INTEGER | |
| raw_hash, extraction_version | TEXT | NOT NULL |

### raw.x_posts

`author_id` deliberately has **no FK** to `raw.x_users` — capture must
never drop a post over referential nicety. `is_official_tier` marks posts
from the verified-officials timeline pull.

| Column | Type | Notes |
| --- | --- | --- |
| tweet_id | TEXT | PK |
| author_id | TEXT | NOT NULL, no FK |
| conversation_id | TEXT | |
| created_at, fetched_at | TIMESTAMPTZ | NOT NULL |
| text | TEXT | NOT NULL |
| lang | TEXT | |
| retweet_count/reply_count/like_count/quote_count | INTEGER | NOT NULL DEFAULT 0 |
| place_id, place_country_code, place_full_name | TEXT | |
| context_annotations | JSONB | |
| in_reply_to_user_id, referenced_tweet_id, referenced_tweet_type | TEXT | |
| raw_hash, extraction_version | TEXT | NOT NULL |
| is_official_tier | BOOLEAN | NOT NULL DEFAULT false |

Indexes: `(author_id)`, `(created_at)`, `(place_country_code)`, partial on
`is_official_tier` where true.

### raw.x_users

| Column | Type | Notes |
| --- | --- | --- |
| user_id | TEXT | PK |
| username | TEXT | NOT NULL |
| name, location, description | TEXT | |
| created_at | TIMESTAMPTZ | |
| followers_count/following_count/tweet_count/listed_count | INTEGER | NOT NULL DEFAULT 0 |
| verified, protected | BOOLEAN | NOT NULL DEFAULT false |
| verified_type, profile_image_url | TEXT | |
| fetched_at | TIMESTAMPTZ | NOT NULL |
| raw_hash | TEXT | NOT NULL |

Indexes: `(username)`, `(created_at)`.

---

## `corpus` — normalized corpus + entity registry

### Enums

- `corpus.political_lean`: `democrat | republican | independent | mixed | unknown` —
  **the single source of truth** for curated political lean, one flat
  convention across the entire schema (owner decision, supersedes the
  plan's original two-column party/lean sketch). One column name (`lean`)
  everywhere this enum appears — `corpus.entities.lean`,
  `analysis.author_leans.lean`, `analysis.narrative_leans.lean`,
  `serving.entity_stance_rollups.lean`, `serving.narrative_rollups.lean`,
  `serving.bot_rollups.lean` — no other table stores curated affiliation,
  and no `party` column exists anywhere in the schema. **Never fed into an
  LLM prompt** (bias/priming risk).
- `corpus.source_type`: `news | reddit_post | x_post`
- `corpus.entity_kind`: `official | collective | outlet | subreddit`
- `corpus.author_tier`: `elected_official | affiliated | general_public`
- `corpus.classification_method`: `curated_list | llm`
- `corpus.platform`: `x | reddit`
- `corpus.admission_class`: `sampled | official_record` — added by
  `0003_admission_class.sql`; see `corpus.documents.admission_class` below.

### corpus.entities

**Curated directly in this table** (owner decision, 2026-07-22: the entity
registry's source of truth moved from YAML-in-git to the database itself,
for readability of hands-on curation). Seeded once from the retired YAML
registries (`data/verified_officials.yaml`, `data/news_outlets.yaml`,
`data/major_subreddits.yaml`, `data/known_political_x_accounts.yaml`) by
`data/pg-migrations/0002_entity_registry_seed.sql`; edits from here on
happen by hand against the DB, and `pg_dump` backups are the curation
history (not YAML diffs). The frozen YAMLs remain in git, read-only, only
for the old SQLite-stack's `analysis/src/reporting/entity_registry.py`
until it retires in Phase 9. Rows are **never DELETEd** — an entity that's
no longer current is flipped `active=false` instead, so historical FKs
always resolve.

| Column | Type | Notes |
| --- | --- | --- |
| entity_id | BIGINT IDENTITY | PK |
| entity_key | TEXT | UNIQUE — stable slug (domain / handle / subreddit name) |
| kind | corpus.entity_kind | NOT NULL |
| display_name, blurb | TEXT | |
| role_title, term_start, owner, source_citation | TEXT/DATE | nullable, populated per kind; source_citation is the curation *citation* (bio_source / AllSides rating text) |
| lean | corpus.political_lean | NOT NULL DEFAULT 'unknown' — curated for every kind: officials/collectives (party membership IS the lean value) and outlets/subreddits (media lean flattened onto the same 5-value enum) |
| lean_source | TEXT | nullable, display/audit only, never a join axis — the *original pre-flattening* classification string (e.g. `"center-left"`, `"R"`, `"independent-dem"`), distinct from source_citation |
| active | BOOLEAN | NOT NULL DEFAULT true |
| editorial | BOOLEAN | NOT NULL DEFAULT false — true for the 3 originally hand-edited registries; false for officials promoted wholesale from `known_political_x_accounts.yaml` (549 people, one entity per person). Partial index `idx_entities_editorial (kind) WHERE editorial` backs the UI's editorial-only filter. |
| elected | BOOLEAN | nullable — curated truth for `account_tier` derivation (`analysis/src/engine/account_tier.py`): TRUE = currently an elected federal officeholder, FALSE = appointed/institutional. Meaningful only where `kind IN ('official', 'collective')`; always NULL for `outlet`/`subreddit`. Hand-editable like every other curated column here; seeded by 0002 (mechanical TRUE for rank-and-file House/Senate members, explicit per-entity judgment for the President/VP/cabinet/agency-head/party-chair/chamber-leadership entries — see the migration's commented classification block). |
| updated_at | TIMESTAMPTZ | NOT NULL DEFAULT now() — last curation timestamp; seeded to a fixed value by 0002, bumped by whatever updates the row thereafter (no sync process) |

**Lean flattening (owner decision, resolved — no longer open, historical):**
the retired YAML curation vocabularies were richer than the 5-value enum
(`left/center-left/center/center-right/right/mixed` for outlets;
`left/center/right/mixed` for subreddits; `R/D/I/independent-dem/other` for
officials). The one-time seed migration flattened them deterministically:
`left`/`center-left` -> `democrat`, `center` -> `independent`,
`center-right`/`right` -> `republican`, `mixed` -> `mixed`; for officials,
`R` -> `republican`, `D` -> `democrat`, `I`/`independent-dem` -> `independent`.
Absent or unrecognized values (including the documented-but-unbucketed
`other` party code) mapped to `unknown`, logged loudly at seed time — never
guessed. The pre-flattening string survives in `lean_source` for audit; the
flat `lean` value is what every join uses. Any lean set by curation from
here on is entered directly as one of the 5 enum values.

### corpus.entity_aliases

Composite PK `(entity_id, alias)` — covers `also_domains`-style alternates.

### corpus.authors

Unified author identity from `x_users_raw`/Reddit metadata/old
`account_profiles`. `UNIQUE(platform, platform_author_id)`. Denormalized
latest-snapshot columns: handle, display_name, description, location,
profile_image_url, verified, verified_type, followers_count,
following_count, account_created_at, last_synced_at.

### corpus.author_profiles

Replaces `account_profiles`. `author_id` UNIQUE FK -> authors. `tier`
(corpus.author_tier), `method` (corpus.classification_method), optional
`entity_id` FK (links an account to its curated registry entity — NULL for
unmatched general-public accounts), `classified_at`, `notes`. Deliberately
**has no party/lean column of its own** — an official's lean is read by
joining `entity_id -> corpus.entities.lean`; storing it here too would
duplicate the single source of truth.

Currently empty and unwritten: the only prior writer
(`analysis/src/etl/registry_sync.py`) is retired, and it could never have
populated this table from a virgin seed anyway (`author_id` is `NOT NULL`
and `corpus.authors` is empty pre-ETL). Populating it is either a direct DB
curation edit once an author exists, or future work for Phase 6's
`account_classifier` — see `docs/todos/pg-redesign.md`.

### corpus.documents

Parent of every analysis FK. `UNIQUE(source_type, natural_key)` is the ETL
idempotency key (`natural_key` = url_canon / fullname / tweet_id).
`source_url NOT NULL` (invariant C1). `raw_hash`, `etl_version` (e.g.
`"pg-1"`) NOT NULL. `author_id` nullable FK -> corpus.authors.

| Column | Type | Notes |
| --- | --- | --- |
| doc_id | BIGINT IDENTITY | PK |
| source_type | corpus.source_type | NOT NULL |
| natural_key | TEXT | NOT NULL, part of UNIQUE(source_type, natural_key) |
| domain_or_subreddit | TEXT | |
| author_id | BIGINT | FK -> corpus.authors, nullable |
| published_at | TIMESTAMPTZ | NOT NULL |
| title, body | TEXT | body NOT NULL |
| source_url | TEXT | NOT NULL |
| raw_hash, etl_version | TEXT | NOT NULL |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT now() |
| admission_class | corpus.admission_class | NOT NULL DEFAULT 'sampled' — added by `0003_admission_class.sql`. `'official_record'` = X post authored by a tracked active official (`corpus.entities` kind='official', active=true, joined via `corpus.author_profiles.entity_id`), admitted by `analysis/src/etl/documents.py` regardless of the 30-day recency window and capped at `OFFICIAL_RECORD_PER_AUTHOR_CAP` per author. Every other doc (news, reddit, non-official X) keeps the default. |

Indexes: `(published_at)`, `(author_id)`, `(domain_or_subreddit)`.

### Subtype tables

Each FKs both the parent document AND its raw row — the old convention-only
`docs.ident` join becomes an enforced FK.

- **corpus.news_articles**: `doc_id` PK/FK -> documents; `url_canon` UNIQUE
  FK -> `raw.articles`; `domain`, `extraction_version`;
  `outlet_entity_id` nullable FK -> `corpus.entities` (`kind='outlet'`),
  resolved at ETL time via `analysis/src/common/canonicalize.py`'s
  `canonicalize_news_domain` against the curated entity/alias set — NULL
  when unmatched (never blocks a doc), backfilled on a later
  `documents.py` run once the outlet is curated into the registry.
- **corpus.reddit_posts**: `doc_id` PK/FK -> documents; `fullname` UNIQUE FK
  -> `raw.reddit_posts`; `subreddit`, `score`, `num_comments`;
  `subreddit_entity_id` nullable FK -> `corpus.entities` (`kind='subreddit'`),
  same resolution/backfill contract as `outlet_entity_id`.
- **corpus.x_posts**: `doc_id` PK/FK -> documents; `tweet_id` UNIQUE FK ->
  `raw.x_posts`; `conversation_id`, `lang`, `place_country_code`,
  `retweet_count`, `reply_count`, `like_count`, `quote_count`,
  `is_official_tier`, `referenced_tweet_id`, `referenced_tweet_type` --
  the last two snapshotted from `raw.x_posts` at ETL load time (same as the
  engagement counts) so the citations engine reads them off `corpus.x_posts`
  and never joins `raw.*` at analysis time.

---

## `analysis` — runs + typed per-task results

`analysis/src/results/store.py` owns every run-anchored typed result table
(the tables `RunHandle.save_*()` writes: `sentiment_results`,
`favorability_stances`, `target_mentions`, `propaganda_results` +
`propaganda_techniques`, `claims`, `bot_signals`, `citations`) plus
`analysis.runs`/`analysis.prompt_versions` themselves — store.py is the
only module that ever inserts one of these, and every row traces back to
exactly one `analysis.runs` row via `run_id`. Named aggregate/derived
tables that are NOT run-anchored are owned by the module that computes
them instead, each documented at its own writer: `analysis.
author_bot_scores` (a materialized rollup recomputed wholesale, not
written per-run) by `engine/bot_detection.py::refresh_author_bot_scores()`;
`analysis.clustering_runs`/`narratives`/`narrative_docs` (a batch job over
many docs per invocation, not one run per doc/author -- `results/store.py`'s
`RunHandle` has no narrative-shaped save method) by
`engine/narrative_clustering.py`. Both write their tables directly via
`common/db.py`, documented as an explicit exception at their own module
docstring.

### Enums

`task` (bot/text/targets/propaganda/claims/citations/account_tier),
`run_status` (pending/running/done/failed), `inference_method`
(llm/deterministic/hybrid), `sentiment_label` (positive/negative/neutral/mixed),
`favorability_label` (favorable/unfavorable/neutral/mixed), `bot_label`
(bot/suspicious/human/unknown), `propaganda_technique` (loaded_language/
name_calling/ad_hominem/appeal_to_fear/whataboutism/doubt_casting),
`link_type` (url_citation/quote/reply/retweet), `verdict`
(correct/incorrect/uncertain).

### analysis.prompt_versions

`prompt_version_id` PK; `prompt_version` TEXT UNIQUE (human label, e.g.
`sentiment_v3`); `task`; `system_prompt` NOT NULL; `user_prompt_template`;
`created_at`; `notes`.

### analysis.runs

One row per analysis attempt. `doc_id` XOR `author_id`
(`CHECK ((doc_id IS NULL) <> (author_id IS NULL))`). `model_id` NOT NULL;
`prompt_version_id` nullable FK (deterministic runs have none);
`inference_method` NOT NULL; `raw_response JSONB` = the verbatim audit
payload (never holds error text); `error TEXT` = the failure message for
`status='failed'` runs, NULL otherwise; `is_current` flips transactionally
when a task is reprocessed.

Two partial unique indexes enforce "one current run per subject per task":

```sql
CREATE UNIQUE INDEX runs_current_doc_task_uq ON analysis.runs (doc_id, task)
    WHERE is_current AND doc_id IS NOT NULL;
CREATE UNIQUE INDEX runs_current_author_task_uq ON analysis.runs (author_id, task)
    WHERE is_current AND author_id IS NOT NULL;
```

One run may feed multiple typed result tables (the unified `text` run
writes both `sentiment_results` and `favorability_stances`).

### Result-store write semantics (`results/store.py`)

`results/store.py` is the only writer of `analysis.*` **run-anchored typed
result** tables (see the schema section above for the two named exceptions,
`author_bot_scores` and the narrative tables, owned by their computing
modules); engines call `open_run()` then `RunHandle.save_*()` then
`finish()`, and nothing
reaches Postgres before `finish()`. `finish()` commits the run row plus all
accumulated result rows in one transaction, in this order:

1. **Advisory lock**: `pg_advisory_xact_lock(hashtext(lock_key))`, keyed
   `task:doc_id|author_id:subject_id`. Transaction-scoped (releases
   automatically on commit or crash), so concurrent `finish()` calls for the
   same (subject, task) serialize into ordinary sequential execution instead
   of racing the partial unique index below.
2. **Flip-before-insert**: for `status='done'`, the predecessor row's
   `is_current` is UPDATEd to `false` *before* the new row is INSERTed.
   Insert-then-flip is rejected: Postgres checks a plain (non-deferred)
   unique index per statement, so both rows would briefly satisfy the
   partial unique index between the two statements — a real violation, not
   just a race.
3. **Failed-run rule**: a `status='failed'` run is inserted with
   `is_current=false` unconditionally, never flips a predecessor, and
   discards all accumulated `save_*()` results (nothing is written to the
   typed result tables). Stale-but-valid beats broken: a prior succeeded run
   keeps serving as `is_current` until a new succeeded run replaces it.
4. **Traceability contract**: `model_id` is required unconditionally
   (mirrors the NOT NULL DDL constraint). `prompt_version` is required only
   when `status='done'` and `inference_method` is `llm`/`hybrid` —
   deterministic runs never need one, and a *failed* llm/hybrid run is
   exempt too (it never got far enough to produce a prompted result).

`error`/`raw_response` never mix: `error` is written straight to its own
column, `raw_response` passes through untouched (see the no-mixing note
above).

### Typed result tables

| Table | Key | Notes |
| --- | --- | --- |
| `sentiment_results` | run_id PK | label, score, sarcasm_detected, evidence_spans TEXT[] |
| `favorability_stances` | favorability_id PK | run_id + entity_id FK, stance, score, evidence_spans — one run can carry stance toward >1 entity |
| `target_mentions` | mention_id PK | run_id, doc_id, raw_target (audit, always kept), entity_id nullable (unresolved persists), stance, topic, confidence, evidence_spans |
| `propaganda_results` | run_id PK | density, summary, techniques_validated, techniques_dropped |
| `propaganda_techniques` | technique_id PK | run_id FK -> propaganda_results, technique enum, verbatim evidence_span, confidence |
| `claims` | claim_id PK | run_id, doc_id, claim_text, topic, confidence |
| `bot_signals` | run_id PK | doc_id, label, score, and typed stylometrics: llm_text_likelihood, burstiness, type_token_ratio, template_score (full detail stays in runs.raw_response) |
| `author_bot_scores` | author_id PK | materialized per-author rollup: score, variance, sample_count, bot_post_count, suspicious_post_count, llm_text_likelihood_mean, updated_at |
| `author_leans` | author_id PK | lean, lean_share REAL, lean_confidence, stance_sample_count, computed_at — deterministic, from `engine/lean_derivation.py` |
| `citations` | citation_id PK | run_id (citation_extractor now emits a run row), source_doc_id, target_doc_id XOR target_url, link_type |

### Narratives

- **clustering_runs**: clustering_run_id PK; mode (jaccard/embedding),
  threshold, embedding_model, started_at, completed_at, doc_count —
  versioned provenance per `narrative_clusterer` invocation.
- **narratives**: narrative_id PK; clustering_run_id FK; name, description;
  anchor_claim_id FK -> claims; anchor_embedding REAL[] (pgvector
  deferred); first_seen_at/first_seen_doc_id (earliest doc **we ingested**,
  not claim origin — see CLAUDE.md scope note).
- **narrative_leans**: narrative_id PK/FK; lean, lean_share, confidence,
  doc_count, computed_at — same derivation module as author_leans.
- **narrative_docs**: composite PK `(narrative_id, doc_id)`; discovered_at,
  confidence (the linked claim's own extraction confidence, copied at
  insert time -- NOT the jaccard/cosine comparator similarity, which is
  never persisted), added_by_run FK -> clustering_runs (2026-07-23,
  nullable -- run-precise extension provenance: which run, founding or
  later-extending, discovered this doc<->narrative link).

### Evals

- **evals**: eval_id PK; `run_id` NOT NULL **UNIQUE** FK -> runs; verdict
  enum (correct/incorrect/uncertain); reviewer_id; notes; reviewed_at.
  Verdict on one specific run — append-only-safe.
- **golden_labels**: composite PK `(doc_id, task)`; expected_label;
  source_eval_id FK -> evals; created_at. Run-independent expected answer —
  future reprocess runs auto-score against it (recompute-proof).

---

## `serving` — the rollup seam

One table per panel family, keyed `(window, dimensions)`. Rebuilt per
window inside one transaction (DELETE+INSERT) so the API never reads a
half-built window. `serving.window` enum: `24h | 7d | 30d | 90d`.

Nested drill-down lists that the old `aggregator_models.py` dataclasses
stored pre-nested (byTopic arrays, per-day series, per-sample evidence) are
assembled by the API layer from `analysis.*`/`corpus.*` directly at read
time via these narrow rollup rows — not stored as JSONB (JSONB is
restricted to the three columns listed in the Overview).

| Table | Key | Notes |
| --- | --- | --- |
| `refreshes` | (rollup_name, win) PK | computed_at, row_count, source_max_run_id watermark — the freshness contract, replaces `/snapshot-status` |
| `sentiment_rollups` | UNIQUE(win, dimension, dimension_key) | dimension in overall/platform/topic/time_window/day_of_week; positive/negative/neutral/mixed/volume/net_score/sarcasm_rate |
| `entity_stance_rollups` | UNIQUE(win, tier, entity_id or catch_all_key) | tier in news/officials/public; entity_id nullable for catch-alls; positive/negative/neutral/volume/net_score/engagement_total; **lean, lean_confidence** denormalized from entities/author_leans at build time |
| `sample_docs` | sample_id PK | evidence backing every drill-down; rollup_name+bucket_key identify the panel; doc_id, run_id, label, confidence, reasoning, evidence_spans, **source_url NOT NULL** (invariant C1), rank |
| `narrative_rollups` | (narrative_id, win) PK | supporting_doc_count, net_sentiment, inbound/external_citation_count, propaganda_score, bot_pushed_fraction, mean_confidence, first_seen_doc_id, **lean, lean_share** |
| `propaganda_rollups` | UNIQUE(win, technique) | doc_count, mean_confidence |
| `bot_rollups` | UNIQUE(win, tier, entity_id or catch_all_key) | total_docs, bot_docs, bot_rate_pct, **lean** denormalized |
| `outlet_profiles` | UNIQUE(win, outlet_key, source_type) | net_tone, bot_rate_pct, volume, total_scanned — outlet_key is the raw domain/subreddit, independent of registry match |
| `movers` | mover_id PK | window-over-window tone/favorability deltas; kind in outlet/official/subreddit/favorability |

---

## `ops` — pipeline machinery

- **task_queue**: composite PK `(doc_id, task)`; status (`ops.task_status`:
  pending/in_progress/done/failed); attempts; last_error; claimed_at;
  updated_at. ETL seeds pending rows; workers claim with
  `FOR UPDATE SKIP LOCKED`. Reprocess = UPDATE to pending, never DELETE.
  Index `(task, status)`.
- **pipeline_runs**: pipeline_run_id PK; started_at/completed_at; status;
  `stage_summary JSONB` — per-stage counts/timings for one `job_runner.py`
  invocation.
- **x_api_budget**: month_key ('YYYY-MM' UTC) PK; post_count, user_count,
  request_count, estimated_cents, last_updated.
- **schema_migrations**: version INTEGER PK; applied_at. Ledger for the
  ported Go migration runner.

---

## `archive` — verbatim import of the old SQLite derived data

Read-only by convention: no FKs beyond primary keys, no CHECK constraints,
PKs preserve the imported SQLite integer IDs verbatim. Epoch integers
convert to TIMESTAMPTZ; TEXT JSON columns become JSONB for browsability.
No application code reads or writes these tables except
`tools/migrate_sqlite_to_pg.py --archive` (one-time, Phase 3).

Tables: `docs`, `ai_outputs`, `prompt_versions`, `target_mentions`,
`narratives`, `narrative_docs`, `narrative_citations`, `account_profiles`,
`author_bot_scores`, `doc_task_state`, `ai_output_evals` (expected empty —
production `ai_output_evals` confirmed 0 rows as of the 2026-07-22 plan
pull). Column shapes mirror the v2.0 SQLite tables documented below.

The app role gets `REVOKE INSERT, UPDATE, DELETE` on this schema; the
GRANT/REVOKE statements are a commented template in
`0001_north_star.sql` (role names are a deploy-time decision).

---

## Appendix: v2.0 SQLite schema (live in production until cutover)

The full v2.0 reference — `pages`, `articles_raw`, `reddit_posts_raw`,
`x_posts_raw`, `x_users_raw`, `docs`, `ai_outputs`, `ai_output_evals`,
`prompt_versions`, `narratives`, `narrative_docs`, `narrative_citations`,
`account_profiles`, `author_bot_scores`, `x_api_budget`, `schema_version` —
is preserved in git history at the last commit before this rewrite
(`docs/DATABASE_SCHEMA.md` v2.0, dated 2026-07-09). Consult
`data/migrations/001-025` directly, or `git show <rev>:docs/DATABASE_SCHEMA.md`,
for the column-level v2.0 reference; it is not duplicated here to keep this
document under the terse-target-schema length.
