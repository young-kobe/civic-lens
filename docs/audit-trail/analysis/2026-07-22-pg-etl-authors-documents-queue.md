# 2026-07-22 — Postgres redesign Phase 4: authors/documents/queue ETL

`analysis/src/etl/authors.py`, `documents.py`, and `queue.py` are new — the
Postgres-side replacement for three of `loader.py`'s four responsibilities
(YAML registry sync is a separate, concurrently-landed module,
`registry_sync.py`, not covered here). Together they normalize `raw.*` into
`corpus.authors` / `corpus.documents` + subtype tables, and seed
`ops.task_queue`, against the north-star schema
(`data/pg-migrations/0001_north_star.sql`). This is Phase 4 of the Postgres
redesign (plan `has-our-aggregate-method-async-frog`, checklist
`docs/todos/pg-redesign.md`) — `loader.py` (740 L, SQLite) remains live and
unedited as the behavior reference; it is not retired by this change (that
finishes across Phases 4-7 per the plan).

## What shipped

- **`authors.py`**: `sync_x_authors()` upserts `corpus.authors` from every
  `raw.x_users` row (`ON CONFLICT (platform, platform_author_id) DO UPDATE`
  refreshes the denormalized snapshot columns — corpus.authors is
  documented as latest-snapshot, not history). `sync_reddit_authors()` is a
  documented no-op returning 0: `raw.reddit_posts` carries no author column
  in `0001_north_star.sql`, consistent with the Phase 2 finding that Reddit
  capture is empty in production (datacenter-IP block, not a code bug).
  News outlets deliberately get NO synthetic per-domain author — see
  "Deviations from the task brief" below.
- **`documents.py`**: `load_new_documents()` normalizes `raw.articles` /
  `raw.reddit_posts` / `raw.x_posts` into `corpus.documents` + `news_articles`
  / `reddit_posts` / `x_posts`, porting `loader.py`'s US-politics keyword
  filter, index/hub-page detector, 30-day recency rule, and
  stamp-missing-published_at-with-ETL-time policy. `ETL_VERSION = "pg-1"`.
  Two Phase 4 additions: an additive `domain_filter` (deny/allow/cap)
  section in `data/seeds.yaml`, and `select_within_domain_cap` (see below).
  `author_id` resolves via a read-only `corpus.authors` lookup for X posts
  only (news/reddit always NULL — see deviations); a doc is never blocked
  on a missing author or entity match. `source_url` is built once at ETL
  time (news: the canonical URL; reddit: synthesized
  `reddit.com/r/<sub>/comments/<id>`; X: the handle-independent
  `x.com/i/web/status/<id>` form) since `corpus.documents.source_url` is
  NOT NULL now (invariant C1 enforced structurally, not by read-time
  reconstruction as the old aggregators did).
- **`queue.py`**: `seed_pending_tasks()` inserts one 'pending'
  `ops.task_queue` row per (doc, applicable task) via a single `CROSS JOIN
  unnest(enum_range(NULL::analysis.task))` INSERT, filtered by the
  applicability `WHERE` clause described below
  (`ON CONFLICT DO NOTHING` — reseeding never resets a done/failed/
  in_progress row). `reset_stale_in_progress(older_than_minutes)` resets
  in_progress rows back to pending, preserving `attempts`, clearing
  `claimed_at` — pure queue logic, included here (not the Phase 7
  scheduler) since it has no scheduler-specific state.
- **`data/seeds.yaml`**: additive `domain_filter` section (`deny`, `allow`,
  `max_docs_per_domain_per_window`), seeded with `www.comparecards.com` in
  `deny` (the known 2026-07-22 leak). The SQLite `loader.py` path never
  reads this section.
- Tests: `analysis/tests/test_etl_authors.py`, `test_etl_documents.py`,
  `test_etl_queue.py` — 55 tests total (40 run unconditionally, no DB; 15
  gated on `CIVIC_TEST_DATABASE_URL`, live-verified this task against a
  throwaway `postgres:17-alpine` container with 0001 applied).

## Filter tightening (task-required)

`is_us_political_content` changed from `loader.py`'s raw substring scan
(`keyword in combined`) to a `\b`-bounded regex match
(`_KEYWORD_PATTERN`). The substring form is a systemic false-positive hole:
several keywords are substrings of common, unrelated English words —
`"bill"` inside `"billion"/"billing"` (exactly comparecards.com's
credit-card-content shape), `"vote"` inside `"devote"/"devoted"`, `"poll"`
inside `"polluted"/"pollen"`, `"tax"` inside `"taxi"/"syntax"`, `"law"`
inside `"flaw"/"lawyer"/"outlaw"`. Word-boundary matching closes the whole
class mechanically, without removing or adding a single keyword — genuine
political usage of the same words ("the Senate passed a spending bill",
"she voted for the incumbent") is unaffected; see
`IsUsPoliticalContentTests` for paired regression cases (false-positive
input alongside a genuine-usage input for the same word). Per the task's
own steer ("prefer the deny-list mechanism over clever filter surgery" for
anything speculative), this was chosen specifically because it requires no
guess about which keyword tripped for comparecards.com — the deny-list
entry is the belt, this is the suspenders. `is_index_page` was ported
unchanged — no fixable hole was found there; the task's tightening ask was
scoped to the political-content filter.

## Cap design

`select_within_domain_cap` enforces `max_docs_per_domain_per_window` as a
ceiling on TOTAL `corpus.documents` rows per `domain_or_subreddit` within
the current 30-day window (existing rows already in the window, counted
via `_existing_domain_counts`, plus newly admitted this run) — not a
per-run ceiling, so a prolific outlet can't spread overflow across more,
smaller ETL runs. Selection is deterministic: newest-`published_at`-first
within a domain; undated candidates (which `stamp_published_at` will set to
ETL time) sort as the newest, matching what they become. Applied uniformly
across news domains, subreddits, and X (`domain_or_subreddit = "x.com"` is
a fixed constant for every X post, so a nonzero cap here would also
throttle total X volume, not just per-outlet skew — documented in
`seeds.yaml` and left at 0 pending Kobe's call once real Postgres volume is
visible). 0/absent = uncapped, matching today's behavior.

## Task-applicability matrix (`queue.py`'s seed-query `WHERE` clause)

Read directly off `job_runner.py`'s per-stage `source_type` scoping. Rather
than a matrix data structure, the applicability rules are two `WHERE`
conditions on the single seed INSERT (`t.task <> 'account_tier'` and
`NOT (t.task = 'bot' AND d.source_type = 'news')`):

| task | news | reddit_post | x_post |
|---|---|---|---|
| bot | no | yes | yes |
| text / targets / propaganda / claims / citations | yes | yes | yes |

`bot` excludes news structurally (the 2026-07-11 decision:
"automation rate of an outlet's articles is not a real metric" —
`_get_bot_detection_source_types` enforces this regardless of the
`run_analysis_on` scope knob). The other five apply to every source_type
unconditionally in the queue seed — `run_analysis_on` ("all" /
"social_media" / "x") is a runtime cost-control filter the Phase 7
scheduler's claim query should apply (mirroring `get_unprocessed_docs`'
`source_types=` parameter today), not a seeding-time decision: seeding a
structural superset means a later knob flip needs no re-seed.
`account_tier` is excluded entirely — it is an author-scoped registry
classification (`run_account_classification` seeds curated accounts from
YAML, not a per-doc LLM call), and `ops.task_queue` is a doc_id-only table
(`PRIMARY KEY (doc_id, task)`, `doc_id NOT NULL`) with no author_id column.

## Deviations from the task brief (schema-driven, not chosen)

Both are flagged for Kobe / the `registry_sync.py`/`0001` schema owner, not
resolved unilaterally (0001 is out of scope here):

1. **No per-domain synthetic "news author."** The brief asked for
   `platform='news', platform_author_id=domain` in `corpus.authors`.
   `corpus.platform` (0001) is `ENUM ('x', 'reddit')` — no `'news'` value,
   so this would fail the enum cast. Separately, it would duplicate
   identity the schema already models correctly: outlets are
   `corpus.entities` rows (`kind='outlet'`), and `serving.outlet_profiles`
   already resolves outlet identity by joining
   `documents.domain_or_subreddit` against `entities.entity_key` directly,
   not through an author FK (`outlet_profiles.outlet_key`'s comment:
   "independent of registry match"). News documents get `author_id = NULL`
   by design. Recommended resolution: confirm the domain-join design is
   final (it avoids a second outlet-identity path); the alternative is
   extending `corpus.platform`.
2. **No `news_articles.outlet_entity_id` / `reddit_posts.subreddit_entity_id`
   FK.** The brief asked for these, resolved against `corpus.entities WHERE
   active`. As committed, `corpus.news_articles` / `corpus.reddit_posts`
   carry no such column — outlet/subreddit identity is already resolved by
   domain/subreddit string match at serving-build time (Phase 9), matching
   point 1 above. Not fabricated here since 0001 is a concurrent agent's
   file; flagged for that agent / Kobe to add the column or confirm the
   domain-join design is the intended final shape.

## Deliberately not carried forward from loader.py

- **`metadata_json` enrichment for X posts** (user profile fields folded
  into a JSON blob for the bot detector). The plan retires `metadata_json`
  entirely; `corpus.x_posts` already carries the typed fields it needs, and
  the bot engine can join `corpus.authors` via `author_id` directly
  (Phase 6 work, not documents.py's).
- **Read-time source_url reconstruction** (the old aggregators built X/
  Reddit URLs from `ident` + handle at query time). Built once at ETL time
  instead, since `source_url` is genuinely NOT NULL now.

## Ordering contract

`sync_x_authors()` (authors.py) must run before `load_new_documents()`
(documents.py), which must run before `seed_pending_tasks()` (queue.py).
Each module's docstring states this; nothing enforces it in code (each
function is independently safe to call in the wrong order — it just
silently under-links/under-seeds rather than failing) since Phase 7's
scheduler is what actually sequences pipeline stages.

## Validation performed this task

- 40 unit tests (filter word-boundary regressions — paired false-positive/
  genuine-usage cases for bill/vote/poll/tax/law; index-page detection;
  recency/stamp logic; `DomainFilterConfig` loading incl. the real
  `data/seeds.yaml`; `select_within_domain_cap` incl. tie-break and
  existing-count interaction; URL construction) run with no database.
- Throwaway `postgres:17-alpine` container (`0001_north_star.sql` applied
  cleanly, no changes needed): 15 integration tests — author sync +
  idempotent resync; news/reddit/X doc insertion with correct subtype +
  NULL/resolved author_id; deny-list rejection of a seeded
  `www.comparecards.com` article; cap enforcement (3 candidates, cap 2,
  newest 2 admitted); idempotent re-run (zero delta, no duplicate rows);
  full `authors -> documents -> queue` pipeline FK integrity (doc ->
  author, doc -> subtype, doc -> task_queue rows all resolve).
- Full repo suite (`python -m unittest discover analysis/tests`): 554
  tests, all green, both without `CIVIC_TEST_DATABASE_URL` (the 15 gated
  tests skip) and with it pointed at the throwaway container (all 554 run,
  none skipped).
- Container hygiene: `docker ps -a` / `docker volume ls` confirmed no
  leftover containers or volumes after teardown.

## Follow-ups (tracked in `docs/todos/pg-redesign.md`)

- `registry_sync.py` (YAML -> entities/aliases/curated profiles) is a
  separate, concurrently-owned file — not covered here.
- The two schema deviations above need a decision from Kobe / the
  0001-owning agent (extend `corpus.platform`? add outlet/subreddit entity
  FK columns to the subtype tables, or confirm the domain-join design?).
- `loader.py` retirement spans Phases 4-7 per the plan; not done here.
- Phase 7's scheduler needs to call `reset_stale_in_progress()` at pipeline
  start and apply the `run_analysis_on` cost-control scope at claim time
  (see the task-applicability section above).
- `max_docs_per_domain_per_window` is left at 0 (uncapped) in
  `data/seeds.yaml` pending Kobe's choice of a production value once
  per-domain composition is visible against real Postgres volume.
