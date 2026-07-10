# News visibility in prod

The prod dashboard shows an empty news column because `CIVIC_RUN_ANALYSIS_ON`
defaults to `social_media` (settings.py) and is documented nowhere a deploy
would set it. News docs never receive `ai_outputs` rows, so the
`ai_outputs JOIN docs` in every aggregator drops them.

- [x] Add `CIVIC_RUN_ANALYSIS_ON` to `.env.example` with a comment explaining
      the default excludes news and prod wants `all`.
- [x] Document the variable in `docs/deployment` env reference
      (`docs/deployment/plan.md` env template block).
- [ ] Manual (Kobe, prod host): set `CIVIC_RUN_ANALYSIS_ON=all` in
      `/etc/civic-lens.env`, re-run analyze so the backfill picks up news docs
      (`get_unprocessed_docs` re-queues them per task), confirm
      `SELECT COUNT(*) FROM ai_outputs a JOIN docs d ON a.doc_id=d.doc_id
      WHERE d.source_type='news'` goes above 0 and the news column renders.
