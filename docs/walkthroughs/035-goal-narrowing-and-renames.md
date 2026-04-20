# 035 — Goal Narrowing + Honesty Renames

## Context

A business-logic review (see this branch's history) surfaced a gap between the project's stated goal — *"measure how political narratives propagate across news media and online discourse"* — and what the code actually does. The narrative pipeline (walkthroughs 032–034) is good at clustering repeated claims and surfacing them as a list, but the word "propagate" implied a causal-propagation engine the data cannot support:

- `narratives.origin_doc_id` actually stored the earliest doc *we ingested* carrying a claim, not world-origin.
- Citation edges only land between docs we own (plus URL mentions to external URLs), i.e. a partial link graph — not a full propagation trace.
- Invariant B3 (propaganda detection) was documented as a requirement but never implemented.
- Tab labels and page copy leaked internal vocabulary ("Bot Activity Profiler", "Narratives") that was opaque to non-technical readers.

This walkthrough closes the goal-vocabulary gap: it narrows the stated product goal to *"sampled political discourse with a narrative overlay and a partial citation overlay"*, renames the schema + API + UI so the honest name travels end-to-end, and updates the user-facing labels for accessibility. It does not introduce new features; subsequent walkthroughs in the series (036 onward) do.

## Changes

### Schema

- `data/migrations/009_rename_narrative_origin_column.sql` — `ALTER TABLE narratives RENAME COLUMN origin_doc_id TO first_seen_doc_id`. FKs and indexes preserved. The column semantics did not change; the name now reflects what the value has always been.

### Python

- `analysis/src/engine/narrative_clusterer.py` — `INSERT INTO narratives` uses `first_seen_doc_id`.
- `analysis/src/reporting/aggregators/narrative.py` — SQL and helper method renamed (`_first_seen_info`). Module docstring now describes the aggregator as surfacing the narrative overlay + partial citation overlay rather than "propagation data".
- `analysis/src/reporting/models/aggregator_models.py` — `NarrativeSummary` dataclass fields renamed to `first_seen_doc_id`, `first_seen_source_type`, `first_seen_domain`; `to_dict()` mirrors.
- `analysis/src/engine/citation_extractor.py` — module docstring now explicitly calls the output a *partial link graph* and enumerates what it does and does not capture (news-to-news without URL, social paraphrasing with no link, external→external, etc.) so future readers don't assume full propagation coverage.

### API

Response shape updated via the dataclass change — `/api/narratives` now returns `first_seen_doc_id` / `first_seen_source_type` / `first_seen_domain` instead of the `origin_*` fields. Cache key and endpoint path are unchanged.

### UI

- `ui/src/types.ts` — `NarrativeSummary` interface mirrors the backend rename. Inline comment makes the "first ingested by us, not world-origin" caveat explicit in the contract.
- `ui/src/pages/Narratives.tsx` — replaces the single flat list with two named sections, **News Media Narratives** and **Social Media Narratives**, split by `first_seen_source_type`. A third "Other Narratives" section renders only when there are narratives with no source type. Column headers relabeled: "Origin" → "First Seen"; "Cites→" → "Inbound (partial)" with a title-attr tooltip. Disclaimer rewritten in plain language ("What a narrative is here: …") and now names the content as *political* claims explicitly. The Social Media section carries a method-popover note that author-tier split (elected / affiliated / public) is a future update — this is the explicit handoff to walkthrough 036.
- `ui/src/App.tsx` — tab `Bot Activity Profiler` → `Bot Detector`. Internal file name (`BotActivityProfiler.tsx`) retained; renaming the file is churn without payoff. Header subtitle rewritten from `Media Narrative & Sentiment Analytics` → `Political Media · Narrative & Bot Tracker` so the political framing is visible on every page. `CIVIC LENS` header is now a transparent button that routes back to the home tab.
- `ui/src/pages/BotActivityProfiler.tsx` — disclaimer rewritten in plain language: *"This page flags accounts and posts in our political-content sample that look automated… Treat flags as leads, not verdicts."* Title of the callout changed from "Calibrated Language Notice" to "How to read this page".
- `ui/src/pages/Home.tsx` (new) — landing page that opens by default. Sections: hero paragraph ("Civic Lens is a political media analysis tool…"), a three-step "How it works" strip (Ingest / Analyze / Serve), a tabs grid where each card describes one tab in plain language and navigates to it on click, and a closing "How we keep ourselves honest" card summarizing the invariants (traceability, visible confidence, sample-labeling, walkthrough audit trail).
- `ui/src/pages/index.ts` — exports `Home`.
- `ui/src/pages/PublicSentiment.tsx` — top-of-page sampling banner now leads with *"Sampled political discourse:"* and reiterates US-political scope inline.
- `ui/src/pages/GlobalHeatmap.tsx` — map description now says *"Country-level sentiment of political X posts that carry a geo tag"* and names the coverage gap ("most X posts have no location metadata").

### Docs

- `CLAUDE.md` — project description rewritten to match the narrowed goal; added a paragraph explaining what the goal is *not*; stale `TF-IDF clustering` / `clustering` / `CIVIC_CLUSTERING_THRESHOLD` references (orphaned when walkthrough 029 removed clustering) replaced with the current pipeline stages (`citations, claims, narratives`).
- `README.md` — rewritten top-to-bottom. Goal statement updated; API endpoint table replaced (the old one listed deleted endpoints `/api/stories` and `/api/favorability` and was missing `/api/narratives`, `/api/review/*`, `/api/geo-sentiment`); commands table expanded to include `migrate`, `reddit`, `x`, and task-scoped `analyze`.
- `docs/INVARIANTS.md` — goal paragraph added at top. B3 (Propaganda Detection) now explicitly marked *"planned — not yet implemented"* with a status block pointing to walkthroughs 040 and 041 and enumerating the starting taxonomy (loaded language, name calling, ad hominem, appeal to fear, whataboutism, doubt-casting). The document no longer promises something the code doesn't deliver.
- `docs/ARCHITECTURE_DIAGRAM.md` — mermaid diagram updated: removed the deleted `ClusterEngine` node, added `CitationEngine`, `ClaimEngine`, `NarrativeEngine`, and a unified `TextEngine` node for the sentiment+favorability analyzer. Data-flow narrative rewritten accordingly. A goal paragraph at the top reflects the narrowed scope.
- `.agent/workflows/global.md` — overview line rewritten.
- `.agent/workflows/python-ai-reporting.md` — objective, architecture, data-model sections rewritten to include citations + claims + narratives + review; API endpoint table updated; acceptance criteria gained two honest-naming items.

### Political framing pass

A deliberate copy pass across the user-facing surfaces so a first-time viewer understands this is a *political* media analysis tool, not a generic sentiment tracker. Every home-page card, top-of-page disclaimer, and header subtitle now names the political angle explicitly. This is deliberate positioning, not incidental wording — Civic Lens only ingests US-political sources, and the product copy now matches that scope.

### Pre-036 UI cleanup

- **Global filters hidden on Home.** `App.tsx` only renders `<GlobalFilters>` when `activeTab !== 'home'`. The landing page has no filterable data, so the time/source controls were misleading noise.
- **Global Heatmap unlinked from the tab bar.** The page component (`GlobalHeatmap.tsx`) and the `heatmap` case in `renderPage` are intentionally retained so the view can be re-enabled by re-adding an entry to the `TABS` array — nothing was deleted. Rationale: geo coverage is currently thin enough (most X posts have no location) that the page doesn't pull its weight at the top level.
- **Review tab admin-gated.** New `ADMIN_MODE` flag in `App.tsx`: toggle it on via `?admin=1` (persisted to `localStorage`), off via `?admin=0`. Non-admins do not see the Review tab or the Review card on Home. Admins see both. Current auth story remains "none" — this is UI gating, not security. When real auth lands, replace `ADMIN_MODE` with an authenticated-user check and keep the surface shape identical.
- **Home tab-cards synced with nav.** Removed the Global Heatmap tab-card entirely; Review card conditional on `isAdmin`. If you see a tab-card on Home, it will appear in the nav after click.

## What is deliberately out of scope

- **Author-tier classification for Social Media Narratives** — walkthrough 036. Approach: hybrid (Option C) with a curated electeds YAML for the "elected officials" tier and an LLM-backed classifier for "politically affiliated" (journalists, pundits, strategists, PACs, think tanks); everyone else defaults to "general public". Persisted in a new `account_profiles(author_id, platform, tier, classification_method, classified_at)` table. The Narratives UI already reserves the handoff slot in its method-popover note.
- **Expanding the citation graph** (news-to-news, social paraphrasing, external→external) — intentionally dropped from the roadmap. Under the narrowed goal, the citation overlay is a "partial link graph between owned sources" and is labeled as such; investing in full cross-medium coverage would re-open the propagation-engine scope we just closed.
- **Calibration harness + golden-set pipeline** — deferred until after walkthrough 034's review queue has accumulated labeled rows. Was previously queued as 038 in the pre-035 plan; now slots after the propaganda walkthroughs (see new ordering below).
- **Renaming `BotActivityProfiler.tsx` → `BotDetector.tsx`** — file rename is churn without payoff; the tab label is the thing users see.

## Post-035 walkthrough order

Re-sequenced to insert the tier-classification work before the aggregator-honesty + propaganda pipeline:

| # | Scope |
|---|---|
| 035 | (this) Goal narrowing + `first_seen_*` rename + partial-link labeling + Bot Detector rename + News/Social section split + doc cleanup |
| 036 | Account tier classification: `account_profiles` schema, curated electeds YAML, LLM classifier for affiliated, 3-tier UI breakdown under Social Media Narratives |
| 037 | `inference_method` column on `ai_outputs` + dead heuristic-kwargs cleanup in `analyzer.py` + frontier state-machine CHECK constraint |
| 038 | Aggregator honesty: embedding-mode narrative clustering as default, confidence pre-filtering across aggregators |
| 039 | Cache + versioning + stubs: cache geo-sentiment snapshot, parameterize narrative cache by limit, remove `copyPasteSimilarity` / `linkDomainConcentration` placeholders, complete B1 versioning (user prompt template, model_id, temperature) |
| 040 | Propaganda pipeline — backend: taxonomy (loaded language, name calling, ad hominem, appeal to fear, whataboutism, doubt-casting), prompts + schema, `propaganda_detector.py`, loader + job-runner wiring, tests |
| 041 | Propaganda pipeline — surfaces: `PropagandaAggregator`, `/api/propaganda`, standalone UI tab, review-task extension, tests |
| 042 | Calibration harness over `ai_output_evals WHERE is_golden=1` — runs after the golden set has been populated |

## Verification

- `./civic-ingest.exe migrate` — migration 009 applied cleanly against the dev DB (schema_version advanced 8 → 9).
- `cd ui && npm run typecheck` — clean.
- Python tests on affected modules: `analysis.tests.test_propagation` (23 tests), `analysis.tests.test_rich_aggregators` (4 tests), `analysis.tests.test_review` (7 tests) — 34/34 pass.
- Full suite has two pre-existing failures (`test_api.test_analysis_flow` expects an `r/politics` outlet absent from the test dataset; `test_llm_engines.test_deterministic_fallback_favorability` expects `"Trump"` but the analyzer lowercases entities). Both are unrelated to this walkthrough and existed on the parent commit.

## Deploy note

Run `.\run.ps1 migrate` on the live DB to apply migration 009 before the next API restart. The rename is a single `ALTER TABLE RENAME COLUMN` — existing narratives keep their doc references; only the column name changes.
