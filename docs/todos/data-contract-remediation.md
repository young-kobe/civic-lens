# Data-contract remediation

Fixes for the confirmed findings in `docs/audit-trail/ingestion/2026-07-09-adversarial-review-data-layer.md` (IDs referenced below). Theme: the data layer is a contract between Go and Python — every fix here either repairs a broken contract or adds a check that keeps it honest. D-3 (is_official_tier REPLACE) and D-9 (budget truncation) are tracked in `ingestion-review-remediation.md` (I-6, I-5) — not duplicated here.

## Wave 1 — registry hygiene + enforcement

- [ ] **D-1: add the 12 missing official handles to known_political_x_accounts.yaml.** @RepMikeJohnson, @LeaderJohnThune, @chuckschumer, @hakeemjeffries, @ChairmanWhatley, @kenmartinmn, @AGPamBondi, @Sec_Noem, @KristiNoem, @SecKennedy, @RobertKennedyJr, @HHSGov — with correct chamber/branch sections and tier metadata.
- [ ] **D-7a: resolve the DHS-Secretary contradiction.** `known_political_x_accounts.yaml` lists Markwayne Mullin as DHS Secretary AND sitting OK senator (@SenMullin duplicated; @DHSgov attributed to him), while `verified_officials.yaml` gives @DHSgov to Kristi Noem. Fix to current reality, remove the duplicate row.
- [ ] **D-7b: registry row hygiene.** Fill or explicitly annotate Rep. Christopher Smith's `handle: null`; fix the off-enum `account_type: official/personal` value.
- [ ] **D-1b: make the contracts enforceable.** Add `analysis/tests/test_registry_consistency.py`: (1) every verified_officials handle (primary + alternates) exists in known_political_x_accounts.yaml; (2) no duplicate handles within known-accounts; (3) account_type values within the documented enum; (4) no null handles without an explicit `source_status` note. This runs in existing CI for free and turns three MUST-comments into checks.
- [ ] **D-8: reconcile seeds vs registries.** Add cbsnews.com to news_outlets.yaml (it is a priority-10 seed landing unlabeled docs today); add the 5 seeded-but-unregistered subreddits to major_subreddits.yaml; decide per entry whether the 4 registered-but-unseeded outlets and 3 unfetched subreddits get seeds or get removed. Extend the consistency test to cover seeds-vs-registry in both directions (allow an explicit exceptions list so editorial intent can diverge deliberately).

## Wave 2 — cross-language guarantees

- [ ] **D-5: enable FK enforcement in Python.** One `PRAGMA foreign_keys=ON` in the shared connection helper (`loader.py:102-104` and any other `sqlite3.connect` sites — grep for them). Run the full suite after: latent orphan-writing bugs will surface as FK errors, which is the point.
- [ ] **D-6: make migrations transactional.** `db.go:81` — wrap each migration file + its version INSERT in one transaction; strip the manual `COMMIT` from 017/018 so the wrapper owns it. Test: inject a failing statement mid-migration, assert schema_version unchanged and re-run succeeds.
- [ ] **D-12: record migration 004's version row.** Add the missing INSERT (guarded so already-migrated DBs with version 5+ do not re-apply 004; simplest: backfill `INSERT OR IGNORE INTO schema_version (version) VALUES (4)` in the next new migration).

## Wave 3 — schema truth

- [ ] **D-2: regenerate DATABASE_SCHEMA.md from the migrations** (tables, columns, FKs, indexes, the `pages` CHECK, `source_type` values including `x_post`) and delete the dropped-table sections and the `file:///c:/Users/...` link. Alternative if maintenance cost is not wanted: delete the file and point readers at `data/migrations/` — but the walkthrough-consolidation todo already assumes a rewrite, so prefer regeneration. Cross-link that todo's checkbox to this one.
- [ ] **D-4 (+ I-6 decision): give is_official_tier a reader or drop it.** Original intent: analysis-side tier routing without re-classification. Either wire `analysis/src/reporting/entity_registry.py` / aggregator joins to consume it, or write migration 020 dropping the column and remove the writer in `x_officials.go`. Decide once, in the same PR as I-6.
- [ ] **D-10: drop place_country_code + idx_docs_country, or land its consumer.** The column is write-only and its stated consumer never existed; the heatmap it served is gone. Dropping is the default; keep only if geo work is actually planned.
- [ ] **D-13: drop docs.fetched_at or start populating it.** Never written, never read. Fold into the same schema-cleanup migration as D-10.
- [ ] **D-11: fix the budget/headcount comments.** `seeds.yaml` line 74 ("$30 cap" vs the real $50) and the "~24 handles" / config.go "roughly 16 handles" claims vs the actual 37 pulled by `AllHandles()`. Comments must match code — operators size spend from them.

## Exit criteria

Registry consistency test green in CI; `go test ./...` green including the migration-crash test; full Python suite green with FK pragma on; regenerated DATABASE_SCHEMA.md spot-checked against a scratch DB built from migrations. Each wave gets an audit-trail entry naming the D-N ids closed. When every box is ticked, delete this file.
