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
- [ ] **GOP favorability rollup.** Restorable without a recompute, but from
      JSONB rather than a typed column. `overall_gop_stance`
      (`favorable|unfavorable|neutral|mixed`) is still a **required** field
      in the text task's LLM schema (`analysis/src/llm/schemas.py`), and
      `engine/text.py` persists the verbatim payload to
      `analysis.runs.raw_response` — so
      `raw_response->>'overall_gop_stance'` on `is_current` text runs is
      literally the same field the retired aggregator read
      (`reporting/aggregators/movers.py::_gop_favorability`). It does NOT
      depend on entity resolution, which is why the absence of
      `kind='collective'` party entities does not block it. Three caveats
      for whoever implements it:
      - Exclude the trivial short-circuit runs. `text.py` finishes those
        with `raw_response=None`, so they have no stance — they must leave
        the denominator, not count as neutral.
      - Filter `inference_method = 'llm'` explicitly if the heuristic
        keyword-proximity fallback (`engine/analyzer.py`) is also writing
        stances; the old aggregator excluded deterministic rows and the
        replacement should match rather than silently widen the base.
      - Consider promoting the field to a typed column on
        `analysis.sentiment_results` if it becomes a hot path — JSONB is
        unindexed here and this is a whole-corpus scan.
- [ ] **Decide the favorability task's future with this query (owner-run, no
      LLM cost).** `analysis.favorability_stances` is GOP-only by prompt
      instruction (`llm/prompts.py:52-53`) and duplicates what the symmetric
      `targets` task already produces for the whole corpus. The cheaper fix
      for the party asymmetry is to DELETE the favorability half of the text
      task and derive party stance from `target_mentions` in SQL, rather than
      adding a symmetric field and paying for a full `text` re-run. Run this
      first — high agreement means deleting is safe; low agreement is itself
      a model-reliability finding and means the two tasks must stay separate:

      ```sql
      SELECT count(*) AS shared_pairs,
             count(*) FILTER (WHERE agree) AS agreeing,
             round(100.0 * count(*) FILTER (WHERE agree)
                   / nullif(count(*), 0), 1) AS agree_pct
      FROM (
        SELECT CASE
                 WHEN f.stance = 'favorable'   AND m.stance = 'positive' THEN true
                 WHEN f.stance = 'unfavorable' AND m.stance = 'negative' THEN true
                 WHEN f.stance = 'neutral'     AND m.stance = 'neutral'  THEN true
                 WHEN f.stance = 'mixed'       AND m.stance = 'mixed'    THEN true
                 ELSE false
               END AS agree
        FROM analysis.favorability_stances f
        JOIN analysis.runs fr ON fr.run_id = f.run_id
             AND fr.is_current AND fr.task = 'text' AND fr.status = 'done'
        JOIN analysis.target_mentions m ON m.doc_id = fr.doc_id
             AND m.entity_id = f.entity_id
        JOIN analysis.runs tr ON tr.run_id = m.run_id
             AND tr.is_current AND tr.task = 'targets' AND tr.status = 'done'
      ) x;
      ```

      The two vocabularies differ (`favorable/unfavorable` versus
      `positive/negative`), which the CASE maps; a near-empty `shared_pairs`
      is itself the answer — it would mean the two tasks rarely resolve the
      same entity on the same doc, and neither can substitute for the other.

- [ ] **Labeling review for the GOP rollup (do before it ships).** The
      schema asks for `overall_gop_stance` with **no Democratic
      counterpart** — there is no `overall_dem_stance` field. Any restored
      surface therefore measures one party only and must be captioned so it
      cannot be read as an overall political-mood number
      (`.agent/rules/media-analysis.md` rule 5, no universal claims about
      national sentiment). Either caption the asymmetry explicitly or add a
      symmetric field and re-run `text` — an owner decision, not an
      implementation detail.

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
