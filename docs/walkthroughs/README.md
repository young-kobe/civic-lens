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
