# 2026-07-10 — Curated-account routing, received-tone enrichment, drill-down reads, evals overlay

The sentiment aggregator now consumes three tables it previously ignored —
`account_profiles`, `author_bot_scores`, and `narrative_docs` — and two new
read paths expose what the snapshot cache pre-truncates: row-level entity
drill-down and the human-review agreement overlay.

## Public/officials column: curated accounts get named cards

`_route_and_record` (aggregators/sentiment.py) takes the author's
`account_profiles` classification (joined via the new
`ACCOUNT_PROFILE_JOIN_SQL` fragment in `aggregators/base.py`). An X author
who isn't in the editorial registry but is affirmatively classified by the
curated political-accounts list now gets a named per-account card
(`kind='account'`, profile built by `entity_registry.account_profile_dict`,
blurb names the classification source) instead of collapsing into the
"Other X users" catch-all; `tier='elected_official'` additionally upgrades
the row into the officials column. The catch-all now holds only genuinely
unclassified authors, and its blurb says so. Topic three-way tier volumes
count the upgraded rows in their new tier.

## Received tone: reach, speakers, narratives, account-level bot exclusion

`_merge_target_tone` fan-out enrichments, all read-side (no new LLM calls,
no migrations):

- **Engagement-weighted net** — each (doc, target) pair carries weight
  `1 + ln(1 + retweets + replies + likes + quotes)` (`_engagement_weight`;
  counts pulled via COALESCE so non-X docs weigh 1). Emitted as
  `received.engagementWeightedNet`, suppressed under the same
  `MIN_TARGET_SAMPLE_N` floor as the raw net — weighting re-emphasizes
  within a sufficient sample, never rescues a thin one. The formula is
  published verbatim in `targetTone.engagementWeight` (reach proxy, per
  media-analysis labeling rules).
- **By speaker tier** — `received.bySpeakerTier` splits each official's
  received tone by WHO is talking: `officials` (registry match, provenance
  flag, or curated elected_official), `affiliated` (curated), `news`,
  `public` (`_speaker_tier`). Per-cell suppression.
- **By narrative** — `received.byNarrative` joins target docs to
  `narrative_docs`/`narratives` (`_fetch_narrative_doc_map`, window-bounded,
  degrades to empty when the tables don't exist) and surfaces the top
  `MAX_NARRATIVES_PER_TARGET` claims driving the mentions. Per-cell
  suppression.
- **Account-level bot exclusion** — pairs authored by an account whose
  `author_bot_scores.score >= BOT_SCORE_AUTHOR_EXCLUSION` (0.5) are
  withheld from received-tone denominators (`get_high_bot_score_author_ids`
  in base.py) and counted in `targetTone.botExcludedMentions` so the
  exclusion is auditable, never silent. Complements the existing doc-level
  bot exclusion: it catches accounts whose pattern-of-posting scores
  bot-like even when individual posts pass.

## Row-level drill-down reads

`reporting/entity_posts.py` — `fetch_entity_posts(db_path, kind, key,
window, limit, offset)` returns the full, paginated, newest-first list of
classified posts behind one entity card. Filtering mirrors the aggregator
exactly (same registry matching, same catch-all exclusions including the
curated-accounts carve-out, SQL versions of the bot and confidence floors,
latest-output-per-doc dedup), so a card and its drill-down cover the same
posts. Page size capped at `MAX_PAGE_SIZE` (50). Consumed by the API's
`/entity-posts` (see api entry).

## Evals feedback loop closed

`ReviewService.get_public_accuracy()` (reporting/review.py) shapes
`ai_output_evals` into a public per-task agreement payload — n reviewed,
n scored, % marked correct — with accuracy suppressed below
`MIN_PUBLIC_REVIEW_N` (20) scored reviews. Reviewer identity and per-row
labels stay behind the admin-gated review endpoints.
`job_runner.save_snapshots()` caches it under `eval_accuracy` (not
windowed: evals describe the models, not the window).

## Narrative citation semantics

`NarrativeAggregator._citation_detail` decomposes the flat inbound count:
`inbound_by_link_type` (url_citation / quote / reply / retweet),
`external_citation_count` (outbound edges to `target_url`s we never
ingested), and `cross_narrative_citations` (top narratives this one cites
into / is cited by, direction-tagged, capped at 5). All fields stamped on
`NarrativeSummary`. Scope discipline: edges between sampled docs only,
never origin/propagation claims (walkthrough 035).

## Tests

- `test_sentiment_entity_routing.py` — affiliated → named public card,
  elected_official → officials upgrade, catch-all keeps only unclassified,
  tier volumes.
- `test_target_tone_aggregation.py` — engagement weighting math +
  never-rescues rule, speaker-tier split, narrative attribution,
  bot-scored-account withholding with metadata count.
- `test_entity_posts.py` — pagination/order, latest-output dedup, per-kind
  filters, catch-all exclusions, server-side limit cap.
- `test_citation_detail.py` — link-type split, external separation,
  cross-narrative directions, cutoff.
- `test_eval_accuracy.py` — publish-above-floor, suppress-below-floor.

Fixtures for the sentiment aggregator now create `account_profiles` and the
`x_posts_raw` engagement columns (the aggregator joins them unconditionally).

Cross-links: `docs/audit-trail/api/2026-07-10-drilldown-and-eval-endpoints.md`,
`docs/audit-trail/ui/2026-07-10-account-cards-drilldown-overlays.md`,
`docs/audit-trail/infra/2026-07-10-run-analysis-on-env.md`.
