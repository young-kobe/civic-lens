# 2026-07-22 — Postgres redesign Phase 4 closure: lean unification, registry_sync, entity FK restoration

`analysis/src/etl/registry_sync.py` materializes the three editorial YAML
registries (`data/news_outlets.yaml`, `data/verified_officials.yaml`,
`data/major_subreddits.yaml`) plus the curated political-accounts file
(`data/known_political_x_accounts.yaml`) into `corpus.entities` /
`corpus.entity_aliases` / `corpus.author_profiles`, alongside the owner's
decision to flatten political affiliation onto one enum. This entry also
closes the Phase 4 ETL rewrite: it restores the two plan-specified entity FK
columns (`news_articles.outlet_entity_id`, `reddit_posts.subreddit_entity_id`)
that were absent from the first-landed `0001_north_star.sql`, implemented end
to end in `documents.py` and verified in a full clean-room pass. Cross-linked
with `docs/audit-trail/analysis/2026-07-22-pg-etl-authors-documents-queue.md`
(authors/documents/queue), `docs/audit-trail/infra/2026-07-22-pg-phase1-infrastructure.md`
(the DDL this builds on), `docs/audit-trail/infra/2026-07-22-sqlite-to-pg-migration-tool.md`
(Phase 3), and `docs/audit-trail/ingestion/2026-07-22-pg-ingestion-port.md` /
`pg-migration-runner.md` (Phase 2). Authority: plan
`has-our-aggregate-method-async-frog`, Phase 4; checklist
`docs/todos/pg-redesign.md`.

## Political lean: one stored column, one flat enum, one derivation path

Owner decision, superseding the plan's original two-column party/lean
sketch: `corpus.political_lean` (`democrat | republican | independent |
mixed | unknown`) is the ONLY stored curated-affiliation type in the schema.
`corpus.entities.lean` is the single column every kind uses — for
officials/collectives, party membership IS the lean value; for
outlets/subreddits, the curated 6-/4-point YAML scale flattens onto it.
There is no separate `party` column anywhere.

Mapping constants live in `analysis/src/common/registry.py`
(`LEAN_SCALE_TO_POLITICAL_LEAN`, `PARTY_CODE_TO_POLITICAL_LEAN`):
`left`/`center-left` -> `democrat`, `center` -> `independent`,
`center-right`/`right` -> `republican`, `mixed` -> `mixed`; `R` -> `republican`,
`D` -> `democrat`, `I`/`independent-dem` -> `independent`. The
`independent-dem` case (Sanders/King-style registered independents who
caucus with Democrats) maps to the officeholder's actual registration
(`independent`), not the caucus relationship — the caucus fact survives in
`lean_source`, never discarded. Absent or unrecognized values (including the
documented-but-unbucketed `other` party code) map to `unknown` and log a
warning naming the offending entity — never guessed.

`corpus.entities.lean_source` preserves the ORIGINAL pre-flattening YAML
string (`"center-left"`, `"R"`, `"independent-dem"`) for audit/display only —
never a join axis. Every derived lean (`analysis.author_leans`,
`analysis.narrative_leans`, Phase 6) and every serving rollup that needs a
lean dimension (Phase 9) JOINs `entities.lean` (or the derived tables)
rather than re-deriving it — adding a new lean-aware view is a JOIN, not a
new derivation, per the plan.

**Hard constraint, enforced by convention (not yet by code — flag for Phase
6 review):** political lean is curated data and must NEVER be fed into an
LLM prompt. `lean`/`lean_source` carry priming/bias risk if an engine reads
`corpus.entities` for prompt context and forgets to exclude them — every
future engine that builds prompt context from `entities` rows must exclude
both columns explicitly.

## registry_sync.py contracts

- **Never DELETE, active-flip instead**: an entity absent from the current
  YAML pass gets `active = false`, never removed — historical FKs
  (`target_mentions.entity_id`, `author_profiles.entity_id`, and now
  `news_articles.outlet_entity_id` / `reddit_posts.subreddit_entity_id`)
  always resolve regardless of registry churn. A removed-then-reappearing
  key reactivates (`active = true`) on the sync where it comes back.
- **Zero-write idempotency**: every upsert uses
  `INSERT ... ON CONFLICT DO UPDATE ... WHERE <content columns> IS DISTINCT
  FROM EXCLUDED.<...>` — an unchanged re-sync touches zero rows (`synced_at`
  untouched), not just an idempotent overwrite. Verified in
  `test_registry_sync.py::test_idempotent_rerun_is_a_true_no_op` and in this
  closure's chain-test re-run (see below).
- **Alias reconciliation is a targeted diff**, not delete-then-reinsert:
  `_sync_aliases` inserts only new aliases and deletes only dropped ones,
  since nothing else FKs to `entity_aliases`.
- **`author_profiles` linking self-heals**: a curated political account
  (`known_political_x_accounts.yaml`) whose author has not been ingested
  yet is skipped, not guessed at, and links automatically on a later sync
  once `authors.py` has synced that account.

## This closure's change: entity FK columns restored

`documents.py` (landed by a concurrent workstream) flagged that the plan's
`news_articles.outlet_entity_id` / `reddit_posts.subreddit_entity_id` FKs
were missing from the first-landed `0001_north_star.sql` and deferred the
decision rather than editing a concurrent agent's file. This closure adds
both:

- **DDL** (`data/pg-migrations/0001_north_star.sql`): `outlet_entity_id
  BIGINT REFERENCES corpus.entities (entity_id)` on `news_articles`,
  `subreddit_entity_id BIGINT REFERENCES corpus.entities (entity_id)` on
  `reddit_posts`, both nullable, both indexed
  (`idx_news_articles_outlet_entity`, `idx_reddit_posts_subreddit_entity`).
  `docs/DATABASE_SCHEMA.md` updated to match.
- **Resolution** (`analysis/src/etl/documents.py`): `_backfill_outlet_entity_links`
  / `_backfill_subreddit_entity_links`, each a single `UPDATE ... WHERE
  <fk column> IS NULL` pass over the whole subtype table, run once at the
  end of `_load_news` / `_load_reddit`. Matching canonicalizes the raw
  `domain`/`subreddit` string exactly the way `registry_sync.py`
  canonicalizes the YAML primary key/alias (`registry.canonicalize_news_domain`
  / `canonicalize_subreddit`) — deliberately NOT the `domain_key` the
  deny/allow/cap logic uses, which keeps the raw (possibly `www.`-prefixed)
  casing to match `seeds.yaml` verbatim. One code path covers both "matched
  at insert time" and "a `registry_sync` run added/reactivated the
  outlet/subreddit after the doc was already loaded" (the next
  `documents.py` run backfills it) — no separate resolve-then-insert step.
  Never fails a doc: no match leaves the FK NULL.

This does not change the domain-join design `serving.outlet_profiles`
already uses (`outlet_key` independent of registry match, per that table's
comment) — the FK is an additional, more precise identity path Phase 9 can
choose to read instead of the string join; that choice is Phase 9's, not
made here.

## Accepted deviation: news author_id stays NULL

Wherever this checklist references `authors.py`: news documents get
`corpus.documents.author_id = NULL` by design, permanently — not a gap to
close later. `corpus.platform` is `ENUM ('x', 'reddit')` with no `'news'`
value, and even if it had one, a synthetic per-domain "news author" would
duplicate identity the schema already models correctly (outlets are
`corpus.entities` rows, `kind='outlet'`). Outlet identity for a news doc is
read via `news_articles.outlet_entity_id` (this closure) — a doc-to-entity
FK — never an author FK. This is the accepted final shape, not a deferred
decision.

## Superseded (2026-07-22): registry_sync retired

Same-day owner reversal: the entity registry's source of truth moves from
YAML-in-git to the database itself. `registry_sync.py` described throughout
this entry is deleted; a one-time seed migration
(`data/pg-migrations/0002_entity_registry_seed.sql`) replaces it. Everything
below remains an accurate historical record of how the registry was
populated up to that point (the flattening logic, dedup rules, and FK
contracts it describes are unchanged, just no longer re-run). Full
rationale, what replaced what, and re-verification:
`docs/audit-trail/analysis/2026-07-22-db-native-entity-curation.md`.

## Addendum (2026-07-22): officials-promotion closed

Owner decision: promote-all, with an editorial flag. Every curated account
in `known_political_x_accounts.yaml` is promoted to its own
`corpus.entities` row (`kind='official'`); new `entities.editorial`
(`BOOLEAN NOT NULL DEFAULT false`, `0001_north_star.sql`, partial index
`(kind) WHERE editorial`) is `true` for the 3 hand-edited registries,
`false` for the promoted long tail. UI filtering on it is a later phase.

Entity_key / dedup (`registry_sync.py::_sync_promoted_officials`):
`load_political_accounts` yields one record per X handle, but a person
gets exactly one entity row. Records are grouped by `display_name` (only
the 12 executive_branch entries nest >1 handle per person); the first
handle in each group — the official/institutional account, listed first
by convention — becomes `entity_key`, the rest become `entity_aliases`. A
group is skipped (no promoted row) only if its *primary* handle already
resolves to an editorial entity — an any-handle check was tried and
rejected: `@TheJusticeDept` is an also_handle of the Attorney General's
editorial entity AND a handle of the Deputy AG's (Todd Blanche's) promoted
entry, so any-handle wrongly treated Blanche's group as editorial-covered
and dropped him. A shared non-primary handle is excluded from that
entity's aliases and logged, not silently merged.

## Verification (officials-promotion)

Real-YAML sync, throwaway `postgres:17-alpine`: 21 outlets, 16 editorial
officials, 15 subreddits, 535 promoted officials, 14 editorial-dedup (549
unique people — the person count, not the 568 handle-record count). Zero
unknown leans among rows with an actual party code (the 3 unknowns are the
3 executive_branch entries with no party field). Idempotent re-run:
identical `synced_at`/row count. `test_registry_sync.py` +7 tests
(promotion count, party-code lean, editorial flag both ways,
editorial-overlap dedup, multi-handle-one-entity, idempotent re-run,
author_profiles linkage for a promoted official), 35/35 gated. Full suite:
561 tests, 0 failures, 0 skips. Go unaffected (Python/SQL-only change).

## Verification performed this closure

- **DDL clean-room**: throwaway `postgres:17-alpine` (no repo volumes), the
  real `civic-ingest migrate` binary. First apply clean ("Applying migration
  1"), second apply a true no-op (no "Applying migration" line). Per-schema
  table counts unchanged from Phase 1's baseline — `raw` 5, `corpus` 8,
  `analysis` 18, `serving` 9, `ops` 4, `archive` 11 (the two new columns are
  columns, not tables). `\d corpus.news_articles` / `\d corpus.reddit_posts`
  confirm both new FK columns and their indexes.
- **Full Python suite**: `CIVIC_TEST_DATABASE_URL=... PYTHONPATH=$PWD
  analysis/.venv/bin/python -m unittest discover analysis/tests` — 559
  tests, 0 failures, 0 skips (up from the prior 554; the 5 new gated tests
  in `test_etl_documents.py` cover matched-outlet, matched-subreddit,
  unmatched-stays-NULL (news and reddit), and backfill-after-a-later-
  registry_sync-run).
- **Chain test** (registry_sync with the real `data/*.yaml` -> authors ->
  documents, with fixture `raw.*` rows for a known outlet (`cnn.com`, from
  the real registry) and an unknown domain, -> queue), in one database:
  known outlet resolved a real `outlet_entity_id`; unknown domain stayed
  NULL; the X author FK resolved to a real `corpus.authors` row;
  `ops.task_queue` seeded 16 rows for the 3 loaded docs. Re-running the
  entire chain against the same database produced an identical
  `(doc_count, author_count, queue_count, entity_count)` snapshot —
  zero-delta idempotency across all four stages together, not just each in
  isolation.
- **Go regression**: `cd ingest && go test ./... -count=1` — all packages
  pass; the DDL/documents.py change is Python/SQL-only and does not touch
  Go code, this is a non-regression check.
- **Container hygiene**: `docker ps -a` / `docker volume ls` before and
  after confirm the throwaway container and its (unnamed) data layer leave
  no trace; the pre-existing containers/volumes listed predate this task by
  20 months and are unrelated.

## Follow-ups (tracked in `docs/todos/pg-redesign.md`)

- `entity_kind = 'collective'` population — future decision, open.
- Phase 9 should decide whether `serving.outlet_profiles` moves from its
  domain-string join to the new `outlet_entity_id` FK, or keeps both (the
  FK is additive here, not a forced migration of that rollup's join logic).
- The lean-never-in-a-prompt constraint is a documented convention, not yet
  a structural guard — worth a lint/test in Phase 6 when engines start
  building prompt context from `corpus.entities` rows.
