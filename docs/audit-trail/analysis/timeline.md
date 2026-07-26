# Analysis timeline (pre-2026-07, consolidated)

Condensed record of `analysis/src/` (ETL, engine, LLM clients, aggregators, scheduler) history,
consolidated from the retired `docs/walkthroughs/` linear log (see
`docs/todos/walkthrough-consolidation.md`). Chronological by original walkthrough number; most
entries carry no in-file date. Where an outcome was later reversed or superseded — which is common
here, since this layer absorbed the SQLite→Postgres rewrite, the favorability_stances retirement,
and the jaccard→embedding-only clustering switch — that is called out explicitly.

## 001 — Initial Infrastructure (undated)

Original analysis layer was `analysis-python/` alongside SQLite, with a Streamlit dashboard
(`app.py`) for audit/viz and `INVARIANTS.md` established as the correctness constitution. The
Streamlit dashboard was replaced by React+FastAPI in walkthroughs 002/003.

## 005 — Python Analysis Refactoring (undated)

Introduced dataclass models (`engine/models/`, `reporting/models/`) for sentiment/bot/favorability/
story results and the "dataclass internally, `.to_dict()` only at the API boundary" convention still
in use. The favorability-specific dataclasses from this pass are part of the `favorability_stances`
lineage retired by later commits (superseded by `target_mentions`).

## 006 — Background Analysis Pipeline (undated)

Converted the pipeline from on-demand API compute to scheduled background jobs writing pre-computed
JSON snapshots (`SnapshotCache`, `job_runner.py`) — the "cache is the contract between analysis and
API" architecture CLAUDE.md still names. The Windows Task Scheduler this originally used was later
replaced by cron/systemd (infra); the JSON-snapshot-cache mechanism itself was part of the SQLite-era
stack retired by the Postgres rewrite.

## 007-009 — LLM Integration, Ollama Backend, Robust JSON Parsing (undated)

First LLM integration: Gemini Flash via `llm_client.py`, hybrid deterministic+LLM engines
(`HybridSentimentAnalyzer`, `HybridBotDetector`, a `favorability.py` analyzer), deterministic
fallback when the LLM is disabled. The favorable/unfavorable/neutral/mixed stance classifier here is
the origin point of `favorability_stances`, later retired for `target_mentions`. 008 added Ollama as
a local-inference backend (Jetson Orin Nano) and the `llm/` package shape still current (`base.py`,
`gemini.py`, `ollama.py`, `factory.py` with `get_llm_client()`, selected by `CIVIC_LLM_BACKEND`). 009
hardened `llm/base.py`'s JSON parsing against real-world Ollama output — superseded in spirit by the
later schema-enforced structured output (`llm/schemas.py`), which returns valid JSON directly.

## 010 — Pipeline Improvements (undated, analysis slice)

Added a 40+-keyword US-political-content filter at the ETL layer (`loader.py`), paired with 001's
30-day recency window — the filter CLAUDE.md still describes as normalizing to "~30 days of
US-politics content."

## 011 — Dashboard Fixes (undated, analysis slice)

Fixed a `story.py` bug where API source-type strings didn't match UI color-mapping keys, causing grey
bars. Story clustering itself was deleted wholesale in 029.

## 012-013 — Aggregators Refactor + Foundational Analysis Pass (undated)

012 split the 811-line monolithic `aggregators.py` into the modular `aggregators/` package
(`base.py`, `outlet.py`, `story.py`, `sentiment.py`, `favorability.py`, `bot.py`) that CLAUDE.md's
`reporting/aggregators/` reference still describes, and added a live RealClearPolling scraper
(`polling.py`) feeding GOP favorability — both the scraper and the favorability aggregator are part
of the retired `favorability_stances` feature line. 013 is an earlier foundational pass: initial
rule-based `bot.py`, keyword-based `sentiment.py`, and a TF-IDF + cosine-similarity `clustering.py`
writing to `clusters`/`cluster_assignments` — this lexical clustering is the predecessor fully
retired by 029 (deleted) and later replaced end-to-end by embedding-only narrative clustering
(032-033/039, made mandatory by the current `CIVIC_NARRATIVE_EMBEDDING_MODEL` requirement).

## 014 — Dashboard Data & UX Improvements (undated, analysis slice)

Fixed polling-cache loading and added a Social-vs-News sentiment comparison split by source type.
Part of the retired GOP-favorability/polling feature set.

## 017 — Civic Lens Analysis Redesign (undated, analysis slice)

Fixed X-post sentiment misclassification (corrected the `SOCIAL_PLATFORMS` set) and rewrote
`story.py` to classify clusters by content type (articles/social/mixed). Story clustering itself was
later deleted in 029.

## 018 — Analysis Refinement (undated)

Consolidated scattered frozenset constants from `sentiment.py`/`favorability.py` into
`engine/constants.py` — the still-current pattern of per-subpackage constants modules. Deleted the
standalone `aggregators/favorability.py` (merged into the sentiment aggregator), part of the
favorability lineage later retired outright.

## 020 — X Integration & Global Heatmap (undated, analysis slice)

Added `origin_detector.py` for country detection and a `geo.py` country-sentiment aggregator feeding
a new `/api/geo-sentiment` endpoint. This entire geo-aggregation feature was decommissioned in
walkthrough 066.

## 021 — LLM Reasoning & Sentiment Refactor (undated)

Surfaced previously-discarded LLM `reasoning`/`evidence_spans` through the pipeline; added
`sarcasm_detected`; added a `ClassificationSample` dataclass with per-topic top-5 sample attachment
— the ancestor of the "every number links to a sample" pattern used throughout later UI drill-downs.

## 023-024 — Configurable Analysis Scope + Caching Fixes (undated)

Added the `run_analysis_on` setting (`social_media`/`x`/`all`) and `loader_batch_size` — both still
present in `settings.py` per CLAUDE.md's Configuration section — plus a `job_runner.py` helper
mapping stage to target source types. 024 fixed a cache-key mismatch where
`job_runner.save_snapshots()` didn't pre-compute all three `stories_{window}_{content_type}`
combinations; the story-clustering feature this fix served was deleted five walkthroughs later in
029.

## 025 — Unified Text Analyzer (undated)

Merged sentiment and favorability into one LLM inference pass to halve inference cost: deprecated
`HybridSentimentAnalyzer`/`FavorabilityAnalyzer`, replaced with a unified `Analyzer` parsing one LLM
response into separate `SentimentResult`/`FavorabilityResult` via a merged `TEXT_ANALYSIS_SCHEMA`.
The favorability half is part of the feature line later retired (`favorability_stances` →
`target_mentions`); the "one inference pass, multiple typed results" architecture itself persisted.

## 026 — Audit Remediation: DB throughput (undated, analysis slice)

Batched ETL inserts via `executemany`; enforced `busy_timeout=5000` and WAL mode via a connection
context manager. SQLite-specific tuning, superseded by the Postgres rewrite.

## 028 — Sentiment, Polling, UI Enrichment (undated)

Bumped the sentiment prompt to `text-analysis-v2` with contextual rules and evidence-span
requirements. Follow-up: removed heuristic-signal injection into the LLM user prompt after it caused
contradictory outputs ("LLM prompt poisoning") — poisoned `ai_outputs` rows were deleted and re-run.
Split `reasoning` into independent `sentiment_reasoning`/`favorability_reasoning`; the favorability
half is part of the retired feature line.

## 029 — Clustering Removal & LLM Hardening (undated)

**Deletion event.** Removed TF-IDF/lexical story clustering end-to-end: `clustering.py`,
`story.py`, the `/api/stories` and `/api/run/clustering` endpoints, `clusters`/`cluster_assignments`
DB writers, and stale `stories_*.json` caches — explicitly stated in-file as a full removal, not a
deprecation. In its place, hardened LLM output handling: `_validate_evidence_spans()` drops
sub-4-word or non-substring evidence spans and caps confidence at 0.3 if unverified (the mechanism
behind invariant B2's traceability requirement); added JSON-schema validation
(`SchemaValidationError`); `save_ai_output` now records the exact system prompt used per inference.

## 030 — Audit Remediation, Layers 2-4 (undated)

Migration 005 dropped dead tables (`author_profiles`, `engagement_metrics`, `reddit_comments_raw`,
orphaned `clusters`/`cluster_assignments`); migration 006 promoted `place_country_code` to a
first-class `docs` column (removed entirely with the geo stack in 066); migration 007 added five
tables — `prompt_versions` (wired), `ai_output_evals` (not yet wired), `narratives`/`narrative_docs`/
`narrative_citations` (schema only, wired in 032). Fixed a real `geo.py` bug where `avg_sentiment` was
silently always 0.0 from wrong field names — on code later deleted wholesale in 066.

## 032-033 — Narrative Propagation Pipeline + Embedding Clustering (undated)

Wired three of 030's empty narrative tables: a deterministic `citation_extractor.py` (URL
canonicalization, X reply/retweet resolution) writing `narrative_citations`; an LLM
`claim_extractor.py` (≤3 claims/doc, validated evidence spans); and `narrative_clusterer.py` doing
**Jaccard-over-tokens clustering** (threshold 0.3) into `narratives`/`narrative_docs`. `job_runner.py`
extended to a 7-step pipeline (ETL → bot → text → citations → claims → narratives → snapshots). 033
added the narrative reader surface (`NarrativeAggregator`, `GET /api/narratives`) plus opt-in
embedding-mode clustering (`embed()` on `BaseLLMClient`/Ollama, `narrative_similarity_mode` setting,
migration 008's `anchor_embedding_json`), with Jaccard staying the default. **Both the Jaccard
implementation and its default status are superseded**: 039 flipped the default to embedding, and the
current codebase requires `CIVIC_NARRATIVE_EMBEDDING_MODEL` with no Jaccard fallback at all.

## 034 — Review UI + ai_output_evals Writers (undated)

Wired the writer side of `ai_output_evals` (schema-only since 030): a `ReviewService`
(lowest-confidence-first queue, anti-joined against reviewed rows) backing the human-in-loop review
queue CLAUDE.md still describes. Framed explicitly as the first half of closing the golden-set/
calibration audit gap.

## 035 — Goal Narrowing & Honesty Renames (undated) — rationale still load-bearing

A business-logic review found a real gap between the stated product goal ("measure how political
narratives propagate") and what the data actually supported: `narratives.origin_doc_id` only ever
recorded the earliest doc *we ingested* carrying a claim, never the claim's true origin in the world;
citation edges only link docs the system itself owns, i.e. a partial link graph, not a full
propagation trace; and invariant B3 (propaganda detection) was documented as required but never
built. Rather than build the causal-propagation machinery the old wording implied, this walkthrough
narrowed the product goal itself to "sampled political discourse with a narrative overlay and a
partial citation overlay between owned sources" and propagated that honesty through the whole stack:
`origin_doc_id` → `first_seen_doc_id` (migration 009) across schema/aggregator/API/UI, and
`citation_extractor`'s docstring now explicitly enumerates what the link graph does *not* capture
(news-to-news without a URL, social paraphrasing with no link, external-to-external edges). This is
still current: the `first_seen_*` naming convention is the standing vocabulary everywhere in the
codebase for "first document we ingested carrying a claim," and CLAUDE.md's project description
("we do not claim to measure propagation in the causal sense... 'first seen' refers to
first-ingested-by-us") is a direct restatement of this walkthrough's decision. Expanding the citation
graph to chase "full propagation" was explicitly rejected as reopening scope just closed.

## 036 — Account Tier Classification (undated) — rationale still load-bearing

Follow-on from 035: splits narrative sources further by *who first said the claim*, into elected
officials / politically affiliated (journalists, pundits, PACs, think tanks) / general public. The
chosen design — "Option C," a hybrid of a curated YAML source of truth plus an LLM classifier for
individuals no static list can enumerate — reflects a considered tradeoff: a pure curated list can't
scale to every journalist or pundit, while a pure LLM classifier would be unstable and expensive to
rerun on every author. `data/known_accounts.yaml` (later `data/known_political_x_accounts.yaml`) is
authoritative and always overwrites tier on rerun; the LLM path uses `INSERT ... ON CONFLICT DO
NOTHING` so curated data can never be clobbered and an author is never silently reclassified once
LLM-tagged. Unmatched authors default to `general_public` at the aggregator level, not written to the
DB, to avoid re-classifying the same author every run. New `account_profiles` table (platform,
author_id, tier, classification_method, confidence, reasoning). Reddit was explicitly scoped out —
electeds/PACs are rare there and the ingest pipeline doesn't capture the profile signals (bio,
follower graph, verified flag) the classifier needs. A later same-walkthrough extension fixed a real
path-resolution bug and added faction metadata (party/branch/chamber/state/office) via migration 011,
replacing the starter YAML with a 576-entry roster covering the full 119th Congress. This
curated-list-plus-LLM-fallback pattern and the `account_profiles` table are consumed directly by
040's bot-detection pre-exclusion and 043's propaganda/bot-pushed narrative overlay.

## 037-038 — Dynamic Account Refresh + inference_method Column (undated)

Added `analysis/src/etl/refresh_accounts.py`, scraping UCSD's "Congressional Twitter" libguide to
refresh the Congress section of 036's curated YAML (chosen after pressgallery.house.gov blocked
automated fetches); a pure `merge_members_into_yaml` function preserves existing richer district
codes when a handle still matches, since UCSD only gives state abbreviations. Kept as a manual
operator command, not wired into the automated pipeline. 038 added `ai_outputs.inference_method`
(`llm`/`heuristic`/`deterministic`) so heuristic-fallback rows became distinguishable from confident
LLM rows, and removed dead code in `analyzer.py` where ~10 heuristic signal kwargs were passed into a
prompt `.format()` call whose template only had `{text}` — the computation ran for nothing every
call. `inference_method` is the field 040 later uses to distinguish pre-excluded deterministic bot
rows from real classifications.

## 039 — Embedding-Mode Default + Confidence Pre-Filter (undated)

Flipped narrative clustering's default from lexical Jaccard to embedding mode: Jaccard was silently
splitting synonymous claims ("Trump won PA" vs. "Trump victory in Pennsylvania") into separate
narratives, undermining the Narratives tab's core value. Embedding mode had existed since 033 but
shipped opt-in; falls back to per-claim Jaccard if the embedding call fails. Separately, added an
`aggregation_min_confidence` gate (default 0.5) so aggregators stop letting a coin-flip-confidence
LLM call move sentiment/geo/narrative aggregates as much as a high-confidence one. **Superseded**:
the current codebase has since made embedding mode the *only* mode (`CIVIC_NARRATIVE_EMBEDDING_MODEL`
is required; a backend that cannot embed fails the stage outright) — the Jaccard fallback described
here is gone.

## 040 — Bot Detection Rework (undated)

Reworked the bot detector, which had been tuned for 2018-era spambot signatures and scored nearly all
modern LLM-generated political content as human. Reoriented around stylometric signals (sentence-
length variance, hedge-phrase rate, typographic purity) and X account metadata (follow ratio,
sustained rate, listed count), with de-biasing applied last (government forced to 0.0, business
capped at 0.3). Added pre-exclusion via 036's `account_profiles`: elected/affiliated/
government-verified accounts get a deterministic `human`/1.0 row instead of running the detector, so
the audit trail stays intact. New `author_bot_scores` per-author rollup feeds 043's propaganda
overlay directly. Scoring weights were explicitly flagged as placeholders pending calibration.

## 041 — Cache Coverage + B1 Versioning Completion (undated)

Added snapshot caching for `/api/geo-sentiment` (previously recomputed live every load — moot after
066's decommission). Fixed a cache-key mismatch where the writer hardcoded `narratives_{window}_20`
while the endpoint built a variable-limit key; fixed by caching a window-keyed top-100 once and
slicing at request time. Fixed `prompt_versions.user_prompt_template`, present since migration 007
but never populated — `save_ai_output` now upserts it with COALESCE semantics, closing a gap in the
B1 reproducibility invariant.

## 042-043 — Propaganda Pipeline: Backend + Surfaces (undated)

Landed invariant B3 (propaganda detection), open since 035. Six starting techniques (loaded_language,
name_calling, ad_hominem, appeal_to_fear, whataboutism, doubt_casting), each requiring a verbatim
4+-word evidence span — the enforcement mechanism for B3's "measurable techniques with cited spans"
requirement. LLM-only with no deterministic fallback, on the stated principle that "a fabricated
verdict is worse than an empty result"; hallucinated techniques (bad span, unknown name) are dropped
and the headline score capped or zeroed accordingly. 043 surfaced this into aggregator + API (a
technique breakdown, News-vs-Social split, flagged examples) and added two `NarrativeSummary` fields
— `propaganda_score` (mean across supporting docs) and `bot_pushed_fraction` (fraction of unique X
supporting authors flagged by 040's bot rollup) — the "bot-driven and heavy-propaganda" narrative
overlay. Both fields are `None`, not a fabricated zero, when no relevant data exists.

## 045 — Analysis Audit Remediation (2026-04-20)

Fixed a live confidence-scale bug (LLMs sometimes returned 0-100 into schema fields with
`maximum=1`) via `_coerce_numeric_scales` plus rewritten prompts with explicit decimal-format rules.
Consolidated per-aggregator cutoff-branching into `fetch_task_rows`; computed `bot_docs` once per
snapshot refresh instead of 12 times; fixed an `analyzer.py` hot path building a char→word index once
instead of rescanning per offset (up to 100x per doc). Moved `engine/prompts.py` to `llm/prompts.py`.
Migration 015 added `clustering_mode/threshold/embedding_model` columns to `narratives` so a future
threshold change can't silently reinterpret old anchors; `NarrativeAggregator` docstring changed
"propagation data" → "coverage data" per 035's naming discipline.

## 047 — Pre-Deploy Hardening, PR-A (2026-04-21 launch window)

Python/API-side slice of the pre-deploy security remediation: input validation via `Literal` types
and `Query` bounds; path-traversal hardening on `SnapshotCache._get_path` (rejects `..` and null
bytes); dependency CVE sweep; `/api/cache-status` redaction of absolute VPS paths.

## 049 — Launch (live 2026-04-21, analysis slice)

Production fixes during cutover: Gemini's API actually rejected `minimum`/`maximum` JSON-schema
keywords — stripped from schemas, bounds enforced via prompt text plus engine-side clamping only;
`gemini-2.0-flash` had been deprecated for new users, bumped default to `gemini-2.5-flash`; test-suite
fixes (pytest-vs-unittest mismatch, real sleep-based retry test). Filed follow-ups: replace the
deprecated `google.generativeai` SDK, and skip bot detection for news docs entirely (designed for
social signals; news rows just got wasted heuristic-fallback labels the aggregator already filtered
out).

## 050 — Day-of-Week Sentiment + Distribution Drill-Down (undated, analysis slice)

Added a `byDayOfWeek` sentiment breakdown alongside the existing age-bucket breakdown (weekend-vs-
weekday skew is a legible reader question). Added distribution drill-down samples (15
confidence-sorted docs per intensity bucket) — "the every-number-links-to-a-source invariant in
action." Both piggyback on the existing pass over sentiment rows, no new queries.

## 052 — Source Filter + Label Renames (undated, analysis slice)

Wired a real `source` query-param filter into `SentimentAggregator`/`PropagandaAggregator` via a
shared `source_filter_allowed()` helper — `source=all` still hits cache, other values compute live.

## 054-055 — Entity Registries + Phase 3 Pre-Check Audit (2026-04-22)

Added three YAML registries under `data/`: `news_outlets.yaml` (20 outlets, AllSides partisan_lean),
`verified_officials.yaml` (16 seats, 119th Congress + executive), `major_subreddits.yaml` (10 subs
with tilt) — pure content, 22 schema-only tests, nothing consumed them yet. 055 audited live
`data/civic_lens.db` against these registries before writing aggregator code: news-domain matching
needed `www.` stripping (89.7% match after that), Reddit registry match was 39.4% (accepted as is),
and a blocker surfaced — 0% of X docs matched `verified_officials.yaml` because ingest only ran topic
queries, never per-official timelines — deferred to (and fixed by) 056's `seeds.yaml` change.

## 057-058 — Entity Routing: Sentiment, Narrative, Propaganda (undated)

New `analysis/src/reporting/entity_registry.py`: canonicalizers, frozen `OutletEntity`/
`OfficialEntity`/`SubredditEntity` dataclasses, a registry singleton, `resolve_entity()` tier
classifier, catch-all sentinels — the whole entity system in one file. Wired into
`SentimentAggregator` (new `EntitySentimentItem` model, `byNewsOutlet`/`byOfficial`/`byGeneralPublic`
fields, per-topic three-way divergence splits), then `NarrativeAggregator`
(`first_seen_entity_profile`, `cross_tier`) and `PropagandaAggregator` (`by_news_outlet`/`by_official`/
`by_general_public`). Two intermediate refactor shapes (a `sentiment/` package split, a separate
`entity_routing.py` file) were tried and reverted mid-pass per user feedback that peer aggregators
stay single-file. Deliberately kept the pre-existing `first_seen_tier` (elected/affiliated/
general_public, from 036's curated roster) distinct from the new `first_seen_tier_group` (3-way, from
054's 16-seat registry) rather than unifying them.

## 063-065 — Supporting Docs + Source-Link Invariant (undated, analysis slice)

Added `NarrativeAggregator._top_supporting_docs()` (top 6 by confidence desc) plus
`_build_source_label`/`_build_doc_url` helpers feeding `NarrativeSummary.top_supporting_docs`, then
reused `_build_doc_url` to add outbound source links to propaganda examples — together the backend
for invariant C1 (every per-doc evidence surface must outbound-link to its original source). 064 also
added `PropagandaExample.author_handle` via an X-author join to `_fetch_examples`, the fourth
aggregator independently duplicating that join pattern (flagged for a pending cross-cutting cleanup).

## 066 — Movers + Bot Entity Rollups + Geo Decommission (undated)

Three closeouts. New `MoversAggregator` computes window-over-window tone/favorability deltas via two
SQL passes (current window vs. equivalent preceding window), excluding catch-alls and low-volume
entities. Added `BotAggregator._fetch_entity_rollups()` (`by_news_outlet`/`by_official`/
`by_general_public`), closing 065's deferred item. **Full decommission of the geo-sentiment stack**:
deleted `geo.py`, all `COUNTRY_NAMES`/heatmap-related code and tests, and stale cache files —
rationale given: unlinked from nav, dependent on sparse `place_country_code` data, orthogonal to the
three-way editorial frame the rest of the UI had converged on.
