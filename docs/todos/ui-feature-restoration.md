# UI feature restoration after the Phase 9/10 contract rewrite

Phase 10 removed a set of pre-redesign UI features rather than faking them,
because the Phase 9 API contract had no field carrying their data (see
`docs/audit-trail/ui/2026-07-24-phase10-ui-adaptation.md`, "Removed").

Schema audit 2026-07-25: for most of them the **underlying columns survived
the redesign** — only the API query/model layer stopped selecting them. Those
are restorable as read-side joins over `corpus.*`/`analysis.*` with no engine
change and no recompute. This file separates those from the ones that are
genuinely blocked.

Sequencing: none of this blocks Phase 11 cutover. Land it after.

## Restorable from data already stored (no engine change, no recompute)

- [ ] **Daily tone-trend series.** `analysis.sentiment_results` JOIN
      `analysis.runs` (`is_current`) JOIN `corpus.documents.published_at`,
      grouped by `date_trunc('day', published_at)`.
      `idx_documents_published_at` already exists. Add a per-day series to
      `SentimentPanelResponse` and restore the chart.
- [ ] **Narrative first-seen.** `analysis.narratives.first_seen_at` and
      `first_seen_doc_id` are populated columns — `NarrativeSummaryModel`
      simply does not select them. Model + query addition only. Keep the
      existing first-ingested-by-us labeling (CLAUDE.md scope note).
- [ ] **Bot coordination index + posting-cadence heatmap.** The retired
      `_compute_coordination_index()`
      (`analysis/src/reporting/aggregators/bot/metrics.py`) takes nothing but
      an hour-of-day histogram, which is
      `date_part('hour', documents.published_at)` grouped by author. Pure SQL
      against `corpus.documents` + `analysis.bot_signals`.
- [ ] **Propaganda per-entity leaderboard / three-way grid.** The doc ->
      entity path exists three ways: `corpus.news_articles.outlet_entity_id`,
      `corpus.reddit_posts.subreddit_entity_id`, and
      `corpus.authors` -> `corpus.author_profiles.entity_id`. The
      News/Officials/Public split is `corpus.author_profiles.tier`
      (`elected_official|affiliated|general_public`). Join to
      `analysis.propaganda_results` via `analysis.runs.doc_id`.
- [ ] **Per-entity target tone by topic.** `analysis.target_mentions` already
      carries `entity_id`, `stance`, `topic`, and `confidence` on one row —
      the breakdown is a `GROUP BY entity_id, topic`.
- [ ] **Received tone by speaker tier.** `target_mentions` -> `documents` ->
      `authors` -> `author_profiles.tier`.
- [ ] **Cross-page entity deep-linking / Data Desk cross-signal matrix.**
      Not a data gap — `corpus.entities` has both a numeric `entity_id` and a
      stable unique `entity_key` slug, and the panels each picked a different
      one (`EntityStanceAggregate.entityId` numeric vs
      `EntityBotRate.entityKey` string). Emit **both** on every entity-shaped
      response model, then the client-side join is exact rather than a
      `(kind, displayName)` guess. Do this one first — it is the cheapest and
      it unblocks the matrix.
- [x] **GOP favorability rollup — resolved by deletion, not restoration
      (2026-07-25).** The party asymmetry was real: the text task asked for
      `overall_gop_stance` with no Democratic counterpart, so any surface
      built on it measured one party and could never be captioned as an
      overall political-mood reading. Rather than add a symmetric field and
      pay for a full `text` re-run, the favorability half of the text task
      is gone — `entity_stances`, `overall_gop_stance`,
      `overall_favorability_confidence`, and `favorability_reasoning` are
      out of `TEXT_ANALYSIS_SCHEMA` (prompt version `text-analysis-v5` ->
      `v6`), and nothing writes `analysis.favorability_stances` any more.
      Party stance now derives from `analysis.target_mentions` joined to
      `corpus.entities.lean`, which is symmetric across parties by
      construction and already covers the whole corpus. See
      `docs/audit-trail/analysis/2026-07-25-text-sentiment-only.md` and the
      matching `api/` + `ui/` favorability-retirement entries.

      Two consequences worth knowing before anyone reaches for the old
      field again:
      - Rows written under `text-analysis-v5` still carry
        `overall_gop_stance` inside `analysis.runs.raw_response`. It is
        readable history, not a live signal — the set stops growing at the
        v6 cutover, so a surface built on it silently ages out.
      - The `analysis.favorability_stances` table is deliberately still in
        place (no writer, no reader). Dropping it is a Phase 11 checklist
        item in `docs/todos/pg-redesign.md`.

## Blocked — needs a decision or new computation, not a join

- [ ] **Copy-paste similarity distribution.** `analysis.bot_signals
      .template_score` is a per-doc proxy; the retired panel showed a
      pairwise-similarity distribution. Restoring it means recomputing
      pairwise similarity in the bot engine, not selecting a column.
- [ ] **Per-sample AI labels on cards / per-narrative "why flagged"
      bullets.** The detail exists only inside `analysis.runs.raw_response`
      (untyped JSONB). Fine per-doc — `DocDetailModal` already reads it — but
      aggregating it across docs means querying into JSONB. Decide whether to
      promote the wanted fields to typed columns first.
- [ ] **Engagement weighting.** Available for X (`corpus.x_posts`
      retweet/reply/like/quote counts) and Reddit (`corpus.reddit_posts.score`,
      `num_comments`), but news articles carry no engagement column at all. A
      mixed-source weighted metric would silently mean different things per
      source — needs a decision on scope before implementing.
