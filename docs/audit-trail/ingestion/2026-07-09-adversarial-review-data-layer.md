# 2026-07-09 — Adversarial review: data layer (schema, seeds, registries)

Point-in-time adversarial review of the data layer as the contract between the Go ingestor and the Python analysis code: `data/migrations/001-019`, `data/seeds.yaml`, the editorial registries (`news_outlets.yaml`, `verified_officials.yaml`, `known_political_x_accounts.yaml`, `major_subreddits.yaml`), `data/references/`, and `docs/DATABASE_SCHEMA.md`. Findings only. Method: review agent reconstructed the live schema in a scratch SQLite DB from all 19 migrations and diffed registries programmatically; HIGH findings independently re-verified (registry diff re-run, schema doc re-checked) before recording. Filed under ingestion/ because migrations and seeds are ingestion-owned; registry findings affect analysis equally. Companions: `2026-07-09-adversarial-review.md` (same bucket), `../analysis/2026-07-09-adversarial-review.md`, `../ui/2026-07-09-adversarial-review.md`.

## Findings

### D-1 HIGH CONFIRMED — Officials registry violates its own MUST-contract for 12 of 37 handles

`data/verified_officials.yaml` states "Handles MUST also exist in known_political_x_accounts.yaml". Programmatic diff (re-verified): 12 handles are missing — @RepMikeJohnson, @LeaderJohnThune, @chuckschumer, @hakeemjeffries, @ChairmanWhatley, @kenmartinmn, @AGPamBondi, @Sec_Noem, @KristiNoem, @SecKennedy, @RobertKennedyJr, @HHSGov. Eight of the sixteen editorial officials (Schumer, Jeffries, Thune, Whatley, Martin, Bondi, Noem, RFK Jr.) therefore get no curated `account_profiles` row and fall to general_public tier wherever `account_profiles` is joined, while the `entity_registry` path still matches them — the two tier systems disagree about the most prominent officials on the dashboard. Fix shape: add the missing rows, and add a CI-runnable consistency check so the MUST-contract is enforced, not aspirational.

### D-2 HIGH CONFIRMED — DATABASE_SCHEMA.md is frozen at migration 001 and actively wrong

`docs/DATABASE_SCHEMA.md` ("Last Updated 2026-01-23") documents three tables dropped by migration 005 (`reddit_comments_raw`, `clusters`, `cluster_assignments`) including example queries against them; omits ten live tables (`x_posts_raw`, `x_users_raw`, `prompt_versions`, `ai_output_evals`, `narratives`, `narrative_docs`, `narrative_citations`, `account_profiles`, `author_bot_scores`, `x_api_budget`); omits `docs.source_type='x_post'`, `docs.place_country_code`, `ai_outputs.inference_method`, and the `pages` CHECK; and line 10 links a `file:///c:/Users/...` path from another machine. Regenerate from the migrations or delete — as-is it misleads more than it helps. (Already flagged in `docs/todos/walkthrough-consolidation.md`; this review confirms and extends the damage list.)

### D-3 HIGH CONFIRMED — is_official_tier destroyed by the topic-search REPLACE path

Same defect as finding I-6 in the ingestion review (found independently by both passes): `insertPost`'s `INSERT OR REPLACE` omits `is_official_tier`, resetting the flag to 0 whenever a topic query re-matches an official's tweet in the same run. See `2026-07-09-adversarial-review.md` I-6 for the fix shape.

### D-4 MEDIUM CONFIRMED — is_official_tier has zero readers

`grep -rn is_official_tier analysis/` returns nothing: the only writers are `x_officials.go` and migration 018, whose stated purpose ("downstream stages skip the LLM tier classifier") references a consumer that does not exist — and the LLM tier classifier itself was removed on 2026-04-25 (migration 019). Combined with D-3, the column is both corrupted and unread: nobody would notice. Either wire the analysis layer to read it (the original intent) or drop it.

### D-5 MEDIUM CONFIRMED — Foreign-key enforcement is Go-side only

Go opens SQLite with `_pragma=foreign_keys(on)` (`db.go:35`); the Python layer never issues `PRAGMA foreign_keys` (sqlite3 defaults OFF; zero grep hits in `analysis/src`). Python writes every FK-bearing table (`ai_outputs`, `narratives`, `narrative_docs`, `narrative_citations`, `ai_output_evals`) and runs `DELETE FROM docs ...` (loader.py:127) unenforced — orphaned children are possible, and the "same schema, same guarantees" assumption between the layers is false. Fix: one `PRAGMA foreign_keys=ON` in the shared connection helper.

### D-6 MEDIUM CONFIRMED — Migration runner cannot survive a crash inside most migrations

`db.go:81` execs each migration file without a wrapping transaction; migrations 006/008/011/012/014/015 place bare `ALTER TABLE ADD COLUMN` before the version INSERT, and 017/018 record their version after an explicit `COMMIT`. A failure after the ALTER but before the version row wedges every subsequent `migrate` run with "duplicate column name" until manually repaired — the crash-resumable ingestor is not crash-resumable through its own migrations. Fix: wrap each migration+version-insert in one transaction (SQLite ALTERs are transactional).

### D-7 MEDIUM CONFIRMED — Registries disagree on a cabinet seat; duplicate/off-enum rows

`known_political_x_accounts.yaml` lists Markwayne Mullin both as DHS Secretary (executive_branch, with @DHSgov) and as sitting OK Senator (@SenMullin appears twice; last-write-wins in the classifier upsert), while `verified_officials.yaml` assigns @DHSgov to Kristi Noem. Entity cards and account_profiles can label the same @DHSgov posts as two different people, and Mullin's tier depends on YAML section order. Also: Rep. Christopher Smith (NJ04) has `handle: null` (silently skipped), and one `account_type: official/personal` value is outside migration 011's documented enum with no CHECK to catch it.

### D-8 MEDIUM CONFIRMED — Registry/seed coverage drift in both directions

Programmatic diff: cbsnews.com is a priority-10 RSS seed but absent from `news_outlets.yaml` (its docs land unlabeled — no lean shown, against the labeling discipline); bloomberg.com, nationalreview.com, theatlantic.com, usatoday.com are registered outlets never seeded. Subreddits: seeded `geopolitics`, `moderatepolitics`, `neutralpolitics`, `news`, `worldnews` are missing from `major_subreddits.yaml`; registry `republican`, `libertarian`, `neoliberal` are never fetched. Same fix shape as D-1: a consistency check between seeds and registries.

### D-9 MEDIUM CONFIRMED — X budget truncation

Same defect as ingestion I-5 (independently confirmed; the data pass adds that the officials path undercounts ~20% per batch, ~$5-6/month at 37 handles/day). See `2026-07-09-adversarial-review.md` I-5.

### D-10 MEDIUM CONFIRMED — place_country_code column is write-only; its stated consumer never existed

Migration 006's justification ("the geographic aggregator parses JSON on every query") references nothing in the tree: the loader writes the column, but the only country reader (`bot.py:132`) reads the JSON blob, and no aggregator queries the column — the index is dead. Extends the known todo item; confirmed safe to drop column + index once decided.

### D-11 LOW CONFIRMED — Comments contradict the code on budget and headcount

`seeds.yaml` says "$50/month cap" in the header but "$30 cap" at line 74; both it ("~24 handles") and `config.go` ("roughly 16 handles") undercount the actual 37 handles pulled (16 primaries + 21 alternates via `AllHandles()`). Operators sizing spend from the comments underestimate the officials pass by ~55%.

### D-12 LOW CONFIRMED — Migration 004 never records its version

Scratch-DB run shows `schema_version` = [1,2,3,5,...,19]: migration 004 lacks its version INSERT. Harmless today only because 004 is fully IF NOT EXISTS and 005 drops its tables — but the version table lies, and the pattern invites copy-paste into a non-idempotent migration.

### D-13 LOW CONFIRMED — docs.fetched_at is never written or read

All three ETL INSERT paths omit it; no reader exists; DATABASE_SCHEMA.md documents it as populated. Dead column, NULL on every row.

## What held up

`data/references/` frontmatter all parses and every keyword is regex-safe through `re.escape` + word-boundary matching; `config.go` parses every seeds.yaml key with correct tags, and its documented behaviors (officials path resolution relative to seeds.yaml, the 10-tweet floor padding) match the code exactly; the frontier schema, migration 013's CHECK, and all Python INSERT column lists match the reconstructed live schema; `ai_output_evals` writer is consistent with its constraints.

## Recommended fix order

D-1 and D-7 together (one registry-hygiene pass + a consistency check script that CI can run), D-3/D-4 as one decision (preserve the flag AND give it a reader, or drop it), D-5 (one-line pragma), D-6 (transactional migrations), D-2 (regenerate or delete the schema doc), then D-8/D-10-D-13 as a cleanup batch.
