# Civic Lens - Code Audit Walkthroughs

Development walkthroughs documenting all significant changes to the codebase.
Each document captures what was changed, why, and the verification results.

| # | Walkthrough | Description |
|---|-------------|-------------|
| 001 | [Initial Infrastructure](001-initial-infrastructure.md) | Project scaffolding, Go ingestor, initial database schema |
| 002 | [Frontend API Integration](002-frontend-api-integration.md) | Connecting React dashboard to FastAPI backend |
| 003 | [Dashboard UI Implementation](003-dashboard-ui-implementation.md) | Dashboard layout, card components, chart system |
| 004 | [Go Migration](004-go-migration.md) | Database migration system for Go ingestor |
| 005 | [Python Analysis Refactoring](005-python-analysis-refactoring.md) | Extracting models into dedicated modules, SOLID refactor |
| 006 | [Background Analysis Pipeline](006-background-analysis-pipeline.md) | Job runner, async analysis, cache generation |
| 007 | [LLM Integration](007-llm-integration.md) | LLM client abstraction, Gemini/Ollama support |
| 008 | [Ollama LLM Backend](008-ollama-llm-backend.md) | Local Ollama setup, Jetson Orin Nano config |
| 009 | [Robust JSON Parsing (LLM)](009-robust-json-parsing-llm.md) | Defensive JSON parsing for LLM structured output |
| 010 | [Pipeline Improvements](010-pipeline-improvements.md) | Timestamp filtering, US political content filter, coverage |
| 011 | [Dashboard Fixes](011-dashboard-fixes.md) | Fix favorability error, sentiment distribution, source mix |
| 012 | [Live Polling & Aggregators Refactor](012-live-polling-aggregators-refactor.md) | Live polling stats, modular aggregator architecture |
| 013 | [Analysis & UI Implementation](013-analysis-ui-implementation.md) | End-to-end analysis and visualization flow |
| 014 | [Dashboard Data & UX Improvements](014-dashboard-data-ux-improvements.md) | Data quality improvements, UX polish |
| 015 | [GitIgnore & Security Scan](015-gitignore-security-scan.md) | Secrets audit, gitignore hardening |
| 016 | [Agent Rules & Workflows Update](016-agent-rules-workflows-update.md) | Code style rules, DRY/SOLID enforcement |
| 017 | [Civic Lens Analysis Redesign](017-civic-lens-analysis-redesign.md) | Content hierarchy, bot detection fix, cluster improvements |
| 018 | [Analysis Refinement](018-analysis-refinement.md) | Aggregator constants extraction, code cleanup |
| 019 | [Fix X Data Ingestion Error](019-fix-x-data-ingestion-error.md) | Fix migration error for X/Twitter data |
| 020 | [X Integration & Global Heatmap](020-x-integration-global-heatmap.md) | X post extraction, models directory, heatmap |
| 021 | [LLM Reasoning & Sentiment Refactor](021-llm-reasoning-sentiment-refactor.md) | Sarcasm detection, reasoning transparency, topic visualization redesign |
| 022 | [Struct Receiver Refactor](022-struct-receiver-refactor.md) | Runner package struct-method pattern, encapsulated state |
| 023 | [Configurable Analysis Scope](023-configurable-analysis-scope.md) | Select between reddit, x, or all for processing |
| 024 | [Sentiment Caching & UI Fixes](024-sentiment-caching-ui-fixes.md) | Snapshot caching system, sentiment gauge fix |
| 025 | [Unified Text Analyzer](025-unified-text-analyzer.md) | Combined sentiment and favorability LLM pass |
| 026 | [Audit Remediation](026-audit-remediation.md) | Fix concurrency, DB bottlenecks, and dynamic migrations |
| 027 | [SQLite Optimization](027-sqlite-optimization.md) | Context graceful shutdown and SQLite write performance tuning |
| 028 | [Sentiment Polling UI Enrichment](028-sentiment-polling-ui-enrichment.md) | Polling comparison and sentiment UI enrichment |
| 029 | [Clustering Removal & LLM Hardening](029-clustering-cut-llm-hardening.md) | Cut TF-IDF clustering end-to-end; add evidence-span validation, schema validation, and prompt archival |
| 030 | [Audit Remediation — Layers 2–4](030-audit-layer-2-4-remediation.md) | UI sampling disclaimers; drop dead tables; add narrative + audit schema; promote country_code; fix geo bug |
| 031 | [UI Terminal-Density Refactor](031-ui-terminal-refactor.md) | Tokens-first Bloomberg-terminal aesthetic on white: mono numerics, eyebrow labels, sharper panels; hand-tuned all three pages |
| 032 | [Narrative Propagation Pipeline](032-narrative-propagation-pipeline.md) | Wire citations + claim-based narrative clustering; populate `narrative_citations`, `narratives`, `narrative_docs` |
| 033 | [Narrative Reader & Embedding Clustering](033-narrative-reader-and-embeddings.md) | Narratives UI tab + API + aggregator; opt-in embedding-mode clustering via Ollama (`nomic-embed-text`) so synonyms merge |
| 034 | [Review UI & ai_output_evals Writers](034-review-ui-and-eval-writers.md) | Human-in-loop review queue, golden-set marking, per-task accuracy stats; ReviewService + 3 API endpoints + Review tab |
| 035 | [Goal Narrowing & Honesty Renames](035-goal-narrowing-and-renames.md) | Narrow product goal to "sampled political discourse + narrative overlay + partial citation overlay"; rename `origin_*` → `first_seen_*` across schema/API/UI; Bot Detector rename + plain-language disclaimer; split Narratives into News/Social sections; new Home landing page (explains tool + every tab) with clickable CIVIC LENS header; political framing pass across all pages |
| 036 | [Account Tier Classification](036-account-tier-classification.md) | Hybrid curated-list + LLM classifier for X author tiers; `account_profiles` table; `data/known_accounts.yaml` seed; NarrativeAggregator exposes `first_seen_tier`; Social Media Narratives split into Elected / Affiliated / General Public / Reddit sub-cards |
| 037 | [Dynamic Account Refresh](037-dynamic-account-refresh.md) | Scrape UCSD libguide (senators + reps) to refresh `accounts.congress.{house,senate}` in the known_political_x_accounts YAML; atomic write with diff; preserves existing districts + handle casing; `run.ps1 refresh-accounts` CLI |
| 038 | [Inference Method + Frontier CHECK](038-inference-method-and-cleanup.md) | `ai_outputs.inference_method` column (llm/heuristic/deterministic) distinguishes validated LLM rows from fallback rows; dead heuristic-kwargs cleanup in analyzer.py; `pages.state` CHECK(0..3) via table rebuild |
| 039 | [Embedding Default + Confidence Filter](039-embedding-default-and-confidence-filter.md) | Default narrative clustering to embedding mode (synonyms merge); aggregators gain `aggregation_min_confidence=0.5` floor — low-confidence sentiment and bot flags no longer distort aggregates |
| 040 | [Bot Detection Rework](040-bot-detection-rework.md) | Reorient detector around LLM-driven content: stylometric signals (sentence-length variance, hedge-phrase rate, typographic purity), X account metadata (follow ratio, sustained rate, listed count), verified_type de-biasing, pre-exclusion of electeds/affiliated/gov-verified, per-author `author_bot_scores` rollup, real computations replacing aggregator stubs |
| 041 | [Cache + B1 Versioning Cleanup](041-cache-and-versioning-cleanup.md) | Cache `geo_sentiment_{window}` snapshots; simplify narrative cache to window-keyed top-100 with API-side slice (fixes variable-limit miss); populate `prompt_versions.user_prompt_template` via COALESCE upsert |
| 042 | [Propaganda Pipeline — Backend](042-propaganda-pipeline-backend.md) | LLM-driven per-doc detector for six propaganda techniques (loaded_language / name_calling / ad_hominem / appeal_to_fear / whataboutism / doubt_casting) with verbatim evidence validation; new `run_propaganda_detection` pipeline step; ai_outputs task_type='propaganda' |
| 043 | [Propaganda Surfaces](043-propaganda-surfaces.md) | PropagandaAggregator + `/api/propaganda` + cache per window; new Propaganda UI tab with technique breakdown, news-vs-social split, and evidence-span examples; NarrativeSummary gains `propaganda_score` + `bot_pushed_fraction` (040's bot rollup × 042's propaganda = "bot-pushed + heavy propaganda" narrative overlay); Review queue extended with propaganda task |
| 044 | [Ingest-Layer Audit Remediation](044-ingest-audit-remediation.md) | Applies every §1 finding from the 2026-04-19 audit: dead-code removal (ArticleRaw / RedditComment / duplicate CrawlResult / Reddit OAuth / MaxConcurrentDomain); real `ctx` threaded through crawl + article writer so shutdown is honored; `sync.WaitGroup` drain; `Frontier.EnsureRecovered` shared by all runners; `MarkDone`/`MarkFailed` consolidated; `ClaimItems` uses `UPDATE … RETURNING`; `processPage` decomposed with error-category tagging; `PushLinks` returns categorized `PushStats`; SQLite `busy_timeout` actually applied (modernc `_pragma=...` form); five new frontier tests |
| 045 | [Analysis + API Audit Remediation](045-analysis-api-audit-remediation.md) | Lands §§2, 3, 8, 9 of the 2026-04-19 audit. LLM 0-100 confidence coerce + prompt guardrail; aggregator DRY (`fetch_task_rows`, shared `bot_docs`); ETL N+1 preload; `analyzer._word_offsets` hot-path fix; unified Ollama/Gemini backoff; API `/api/v1` versioning with modular routers (health unversioned, admin/data/review split); `/health` probes DB+cache; per-endpoint pipeline-trigger rate limiting; `Literal` window types; deletes `/api/profiles` and `process_analysis_queue`; narrative clusterer audit metadata (migration 015) + anchor warm-up + `embedding_fallbacks` summary; mean (not max) claim-confidence pooling; drops unused `repost` link_type (migration 016); `BotResult.fallback_reason`; prompts moved to `llm/prompts.py` |
| 046 | [UI Audit Remediation](046-ui-audit-remediation.md) | Closes §4 of the 2026-04-19 audit. `ui/src/theme.ts` centralizes palette tokens so the 15+ hardcoded hex values across pages are gone; PublicSentiment (885→417 LoC), Review (470→222 LoC), GlobalHeatmap (376→242 LoC) broken into subcomponents / extracted styles; `services/fetchJSON` + `services/useFetch` module-level cache keyed by page/window so tab-switch is instant; retry UX unified on `refetch()` (no more `window.location.reload`); accessibility first pass (role=img / role=progressbar / aria-hidden / aria-label on charts, confidence meters, donuts, sparklines); `transformBotData` + `.grid-4` dead code removed |
| 047 | [Pre-Deploy Hardening](047-pre-deploy-hardening.md) | Five-PR remediation of the 04_20 consolidated security audit before first public cutover. PR-A Python/API (validation, CVE bump, rate limits, headers, path hardening), PR-B Go ingest (SSRF redirect guard, body-size caps), PR-C UI (URL token scrub, npm audit gate), PR-D infra-as-code (systemd hardening, Caddyfile, CF-IP UFW, fail2ban), PR-E CI/CD (`.github/workflows/deploy.yml` + restricted `deployment` user with forced-command SSH key). Deploy-day: Cloudflare Access, Authenticated Origin Pulls, Gemini key rotation |
