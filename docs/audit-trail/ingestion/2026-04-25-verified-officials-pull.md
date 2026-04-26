# 2026-04-25 — Explicit verified-officials timeline pull

The X ingestor now walks `data/verified_officials.yaml` on every run and pulls each handle's user-timeline directly through `/2/users/:id/tweets`, instead of relying on the search index's `from:`-clause queries to surface those accounts. Posts pulled this way are tagged with `x_posts_raw.is_official_tier = 1`, so the analysis layer can route officials-tier content without re-classifying it. Cross-link: `docs/audit-trail/analysis/2026-04-25-context-seeding-and-classifier-removal.md`.

## What shipped

- New module `ingest/internal/extract/x/officials.go`:
  - `LoadVerifiedOfficials(path)` parses the YAML, normalizes handles to bare lowercase, returns an empty slice (not an error) for a missing file so a stripped-down dev environment doesn't fail the X run.
  - `Client.UserByUsername(ctx, username)` calls `/2/users/by/username/:username` and returns the hydrated user. Used to resolve handles to numeric `user_id` once per handle; subsequent runs use the cached `x_users_raw` row.
  - `Client.UserTimeline(ctx, userID, max)` calls `/2/users/:id/tweets` with `exclude=retweets`. Reuses the existing `models.SearchResponse` shape and `ToModels()` so post/user persistence is unchanged.
- New runner pass `ingest/internal/runner/x_officials.go`:
  - `XRunner.runOfficialsPass` resolves each handle (cache hit if seen, API lookup + budget hit otherwise), pulls the timeline, and writes posts via `insertOfficialPost` with `is_official_tier = 1`. Per-account failures are logged and stepped over; one suspended handle does not abort the rest of the pass.
  - Officials run **before** topic queries in `XRunner.Run` so a tight monthly budget cannot starve the highest-signal surface we collect.
  - Budget guard on `XBudgetTracker` is checked before every API call (lookup or timeline). Skipped accounts are reported in the run summary.
- Migration `data/migrations/018_x_posts_official_tier.sql`: adds `is_official_tier INTEGER NOT NULL DEFAULT 0` plus a partial index for `WHERE is_official_tier = 1`. Existing rows and any post arriving via topic-search queries keep the default.
- Config `XConfig.OfficialsListPath` (default `data/verified_officials.yaml`) and `XConfig.MaxTweetsPerOfficial` (default 5, padded up to 5 if a smaller value is configured because the X API minimum on the timeline endpoint is 5).
- `data/seeds.yaml` no longer carries the two `from:`-clause officials timeline queries (lines 97 + 102 of the prior version). They were redundant with the explicit pass and only covered the search-index recency window. The eight topic queries remain; they fire after the officials pass.
- Tests:
  - `ingest/internal/extract/x/officials_test.go` covers YAML parsing, handle normalization, missing-file = empty, malformed-YAML = error.
  - `ingest/internal/runner/x_officials_test.go` covers the cached-user-id lookup (case-insensitive, miss returns empty) and that `is_official_tier=1` lands in the row when `insertOfficialPost` runs.

## Why

The previous `from:POTUS OR from:VP OR ...` search query bundled 9–11 handles into a single API call. That was budget-cheap but had three failure modes:

1. The X search index is recency-bounded; off-topic posts older than the index window were never returned. Verified Officials cards in the UI therefore sometimes lacked fresh data, especially for officials whose feeds skewed off the topic axes the search query also tested.
2. We had no way to know *which* handle inside the OR-clause produced zero results — if Speaker Johnson's account renamed mid-week, the rest of the clause kept returning posts and the silent miss was invisible.
3. Downstream tier classification depended on a separate LLM call (`classify_with_llm` in `account_classifier.py`) running over high-volume authors, which doubled the cost surface and added latency for accounts we already knew the answer for.

Pulling user-timelines explicitly fixes (1) and (2) — every miss is logged per-handle — and the `is_official_tier` column lets the analysis layer skip the LLM tier classifier entirely for these posts (see the analysis-layer entry).

## Follow-ups

- Watch the per-account log lines on the first ~5 daily runs to identify any handles in `verified_officials.yaml` that fail lookup (renames, suspensions) and refresh the YAML.
- The user-by-username lookup is billed per-call. Once `x_users_raw` covers the full officials list, subsequent runs are cache-only and free at the lookup step.
- If the budget guard skips the topic-query phase often, drop `max_tweets_per_query` to 6 before raising the monthly cap.
