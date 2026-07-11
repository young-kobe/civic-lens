# 2026-07-11 — Index-page ETL filter; bot detection is social-only

Two data-quality changes from the 2026-07-11 prod incident, where CBS/NPR
section-index pages (nav-menu text) were ingested as articles and then
bot-scored with nonsense indicators. UI entry:
`../ui/2026-07-11-bot-news-column-removed.md`.

## What shipped

- **ETL index/hub-page filter** (`analysis/src/etl/loader.py`):
  `is_index_page(text, url)` — deterministic, no LLM. Signals: sentence-
  punctuation density, Title Case share, root/single-segment hub URLs,
  site-chrome vocabulary (`INDEX_CHROME_TERMS`). Gate sits in
  `_load_news_batch` BEFORE the political-keyword filter, because nav menus
  name political topics and passed it. Drops are counted (`skipped_index`)
  in the ETL summary log — never silent. `ETL_VERSION` bumped to `etl-v2`.
  Existing junk rows age out of the time windows (no one-off cleanup).
- **Bot detection queue is social-only**
  (`scheduler/job_runner.py::_get_bot_detection_source_types`): bot_detection
  only queues `reddit_post|reddit_comment|x_post`, intersected with the
  configured `CIVIC_RUN_ANALYSIS_ON` scope. News docs are never scored —
  the LLM calls are saved entirely.
- **Bot aggregator ignores news** (`aggregators/bot.py`): both scans
  (overview + entity rollups) add `d.source_type != 'news'`, so legacy news
  bot rows in `ai_outputs` drop out of the automation-rate denominator,
  indicator frequencies, narrative amplification, and the grid.
  `by_news_outlet` stays in the payload, permanently empty, so stale caches
  and older UI builds keep parsing.
- Tests: `TestIndexPageDetector` + `TestIndexPageFilterWiring`
  (`tests/test_loader.py`, seeded with the real CBS/NPR junk shapes — the
  suite fails if a genuine article would be dropped);
  `TestBotDetectionQueueScope` and the rewritten news-rollup contract test
  (`tests/test_bot_rework.py`).

## Why

- The index-page leak polluted every metric: a nav-menu scrape scored
  NEGATIVE moved an outlet's tone, junk text could glom onto narrative
  clusters, and every junk doc burned LLM budget across all five tasks.
- "Automation rate of an outlet's articles" is not a real metric — articles
  are not accounts. `get_bot_flagged_doc_ids` already treated news as
  human-authored by contract; now the scoring and the display match it.
  The audit ethos deletes a bad metric rather than approximating it.

## Follow-ups

- None. Prod junk rows age out within the 30-day window.
