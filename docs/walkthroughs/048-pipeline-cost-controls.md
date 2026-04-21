# Walkthrough 048 — Pipeline Cost Controls + Seed Refresh

Ships a hard spend cap on the X API runner and refreshes the news / subreddit / X-query seed set for the first production deploy. Target operating cost: ~$50/month all-in, of which $25 is the X API ceiling. Everything else (Hetzner VPS, Gemini, R2 backups) was already bounded; X was the one pipeline with no month-scoped enforcement.

## Why now

During the deploy-readiness review (after walkthrough 047), we caught that:

1. `seeds.yaml`'s comment line `# ~70 requests/hour = ~$50/month at $0.015/100 tweets` cited a pricing model that doesn't exist. X moved to prepaid credits with pay-per-resource (`$0.005/post read`, `$0.010/user read`). Our config would have pulled ~63k tweets/month — way past any reasonable budget.
2. `max_requests_hour` only paces *within a session*; across daily runs there was zero memory of month-to-date spend. A loop bug or a misconfigured timer could have burned a full month's credits in one run.
3. The X query list (21 active) had duplicates (`MAGA` subsumed by `Trump OR MAGA`), hyperlocal noise (`Minnesota`, `Minneapolis`), and missed major political drivers (abortion/SCOTUS, inflation, climate).
4. Subreddit list weighted toward the low-signal end — `r/Liberal` is effectively dead, and balanced structured-viewpoint subs (`AskConservatives`, `AskALiberal`) were missing.

## What landed

**Migration 017** — `data/migrations/017_x_api_budget.sql`. New table `x_api_budget(month_key, post_count, user_count, request_count, estimated_cents, last_updated)`. One row per UTC calendar month. Primary key on `month_key` so month rollover starts fresh.

**Budget tracker** — `ingest/internal/runner/x_budget.go`:
- `XBudgetTracker` loads (or lazy-inserts) the current-month row, carries the tally in-memory during a runner invocation, and persists back on every `Record()` call.
- Pricing constants (`centsPerPost=5`, `centsPerUser=10`) are per-resource, tracked here because they're X-side facts — if X changes pricing, this file is the one place to bump.
- `OverBudget()` checks against the caller's ceiling (`cfg.X.MonthlyBudgetCents`). Ceiling of 0 disables the guard — dev escape hatch.
- `Record(posts, newUsers)` updates counters + persists. Callers pass *newly hydrated* users, not total users in a response, because X charges per unique user per call.
- `Summary()` returns a value type safe for logging and the admin UI.

**Runner integration** — `ingest/internal/runner/x.go`:
- Constructs the tracker once per `Run()`.
- Logs the pre-run MTD state so `journalctl -u civic-lens-x` shows "$X.XX of $Y.YY used" on every execution.
- Checks `OverBudget()` at the top of each query-loop iteration. When hit, logs "ceiling hit — skipping remaining queries" and breaks. Partial progress (already-completed queries in the same run) is preserved in the DB.
- Records spend *before* inserting posts/users into local SQLite, because the API call already spent the credits whether or not local inserts succeed. Forgetting to record on a failed insert would let the ceiling drift across runs.
- Logs post-run state so you can watch spend accumulate across the month.

**Config knob** — `ingest/internal/config/config.go`:
- New `XConfig.MonthlyBudgetCents int` YAML field. Defaulted to 0 in `Load()` via Go zero-value (disables guard). `seeds.yaml` sets `2500` in production.

**Tests** — `ingest/internal/runner/x_budget_test.go`:
- `TestXBudgetTracker_InitInsertsMonthRow` — first-run creates exactly one row keyed by the UTC month.
- `TestXBudgetTracker_RecordAccumulates` — two successive Record calls sum correctly in memory and persist to SQLite.
- `TestXBudgetTracker_OverBudgetHonoursCeiling` — ceiling=40 cents, record 100 tweets + 20 users = 70 cents, OverBudget() goes true.
- `TestXBudgetTracker_CeilingZeroDisables` — ceiling=0 never trips even with 10k tweets.
- `TestXBudgetTracker_PersistsAcrossInstances` — second tracker constructed in the same month picks up accumulated state (simulates mid-month timer re-fires).
- `TestXBudgetTracker_MonthRollover` — tracker constructed at May 1 00:00 UTC sees a fresh $0 counter, not April's balance.

Clock is injected via a `func() time.Time` parameter in the testable constructor so month-boundary behavior is asserted without `time.Sleep`.

**Seed refresh** — `data/seeds.yaml`:
- **X queries 21 → 8**. Dropped duplicates (`MAGA` vs `Trump OR MAGA`), hyperlocal (`Minnesota`, `Minneapolis`), market noise (`Dollar`, `Stock`, `Market`), and all the commented-out drafts. Added `"abortion OR SCOTUS OR Roe"` and `"inflation OR economy"` — both top-five political drivers missing from the original list.
- **`max_tweets_per_query` 100 → 10**. Primary lever for fitting $25/month: 10 tweets × 8 queries × 30 days = 2,400 posts ≈ $12 Posts cost + ~$12 amortized User cost = ~$24/month. Bump this back toward 25 if you increase `monthly_budget_cents`.
- **`max_requests_hour` 70 → 30**. Session pacing only; the real limit is `monthly_budget_cents`.
- **RSS +5 feeds**: AP (neutral wire baseline), Axios Politics, WSJ Opinion, The Federalist, The Intercept.
- **Subreddits rebalanced**: dropped dead `r/Liberal`; added `democrats` (pairs with `Conservative`), `AskConservatives` + `AskALiberal` (structured-viewpoint, high-signal), `PoliticalHumor`.
- Cost comment at the top of the file now reflects reality.

## Cost model (at production config)

| Line | Monthly |
|---|---|
| Hetzner CPX21 + backups | $17 |
| R2 off-site backups | $1 |
| Gemini (~500 docs/day @ $0.0002/doc) | $5 |
| X API (ceiling-enforced) | ≤$25 |
| **Total** | **≤$48** |

The $25 X ceiling is a cap, not a spend — actual use depends on how many unique authors appear in the pulled tweets. Expect the first month to run 70-90% of ceiling as the user-dedup pool warms up, then stabilize.

## Verification

- `go test ./internal/runner/...` — 5 new tests pass (covers init, accumulation, ceiling, zero-ceiling bypass, cross-instance persistence, month rollover).
- `go test ./...` — full suite clean; no regressions in frontier / httpclient / db packages.
- Manual schema check: `sqlite3 data/civic_lens.db ".schema x_api_budget"` after `civic-ingest migrate` shows the new table with primary key and defaults.
- Smoke: running `civic-ingest x --config data/seeds.yaml --db <path>` prints `X budget 2026-XX: $0.00 of $25.00 used (0 posts / 0 users / 0 reqs)` at start and the corresponding "now" total at end.

## Operational notes

- **Manual cap override**: to temporarily widen the budget for a backfill, bump `x.monthly_budget_cents` in `seeds.yaml`, redeploy, run the X timer, then restore.
- **Reading MTD spend**: `sqlite3 data/civic_lens.db "SELECT * FROM x_api_budget ORDER BY month_key DESC LIMIT 3"`.
- **Resetting** (emergency): `DELETE FROM x_api_budget WHERE month_key = '2026-05'`. Runner will re-insert at zero on the next run. Don't do this to evade the cap — it's meant for recovery from data corruption.
- **Future UI**: the summary struct exposed by `XBudgetTracker.Summary()` is JSON-serializable; a later admin endpoint (`/api/v1/x-budget`) can surface MTD spend on the Review or a new Cost tab. Not in this walkthrough.
