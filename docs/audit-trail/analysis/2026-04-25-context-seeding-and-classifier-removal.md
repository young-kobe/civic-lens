# 2026-04-25 — Reference-context seeding + LLM account-classifier removal

Two coupled changes on the analysis layer: text-analysis and claim-extraction prompts now optionally carry a `REFERENCE CONTEXT` block citing authoritative external sources (CDC, IPCC, NAS, BLS, FRED) when the doc's text matches a registered topic; and the LLM-driven account tier classifier was deleted in favour of `verified_officials.yaml` + the curated `known_political_x_accounts.yaml` loader. Cross-link: `docs/audit-trail/ingestion/2026-04-25-verified-officials-pull.md`.

## What shipped

### Reference-context seeding

- New module `analysis/src/llm/context_seeds.py` loads `data/references/*.md`, each with YAML frontmatter (`topic`, `source`, `source_url`, `last_verified`, `applies_to_keywords`) plus a markdown body. Module-level cache is populated on first call and held for the lifetime of the process.
- Five initial reference files:
  - `vaccines.md` (CDC / NAS / WHO on routine immunization safety + the Wakefield retraction).
  - `climate-change.md` (IPCC AR6 SYR — anthropogenic warming).
  - `evolution.md` (NAS / IOM "Science, Evolution, and Creationism").
  - `unemployment-rate.md` (BLS Current Population Survey, U-3 / U-6 explanation).
  - `inflation-rate.md` (BLS CPI-U + the Fed's preferred PCE measure on FRED).
- Matching is word-boundary, case-insensitive (`re.search(r"\bword\b", text.lower())`). Capped at `MAX_SEEDS_PER_DOC = 3` to bound prompt length on docs that touch several topics.
- Injection: `analysis/src/engine/analyzer.py` and `analysis/src/engine/claim_extractor.py` call `match_seeds(text)` and prepend `format_seeds_block(seeds)` above the existing `*_USER_PROMPT_TEMPLATE`. When nothing matches, the block is the empty string and the prompt is byte-identical to the prior version — unrelated docs pay zero token cost.
- Both system prompts gained a small `REFERENCE_CONTEXT_ADDENDUM` (defined once in `prompts.py`) telling the model the block is informational and that the scoring rubric above does not change with or without it. Sentiment / favorability / claim rubrics were NOT modified; this addendum only describes the new optional input.
- Prompt versions bumped in `analysis/src/llm/prompts.py`: `text-analysis-v3` → `text-analysis-v4`, `claim-extraction-v2` → `claim-extraction-v3`. `bot-v2` and `propaganda-v1` are unchanged because those stages do not see the block.
- Audit metadata: matched seed slugs (e.g. `["vaccines", "climate-change"]`) are persisted into `ai_outputs.output_json` — under `deterministic_signals.reference_seeds` for sentiment/favorability rows, and under the new top-level `reference_seeds` key for claim rows.

### Account-classifier collapse to curated-only

- `analysis/src/engine/account_classifier.py` is now curated-YAML only. Removed: `classify_with_llm`, `_load_unclassified_authors`, `_recent_posts`, `_classify_single`, `_persist_llm`, `ClassificationResult`, all LLM-related constants (`MIN_POSTS_FOR_LLM_CLASSIFICATION`, `LLM_LOOKBACK_SECONDS`, `MAX_LLM_CLASSIFICATIONS_PER_RUN`), the `llm_enabled` constructor argument, and the lazy LLM client init. `load_curated`, `_parse_curated_yaml`, `_strip_handle`, `_resolve_x_author_id` and the `CuratedEntry` dataclass remain.
- `analysis/src/llm/prompts.py` lost `ACCOUNT_CLASSIFIER_SYSTEM_PROMPT`, `ACCOUNT_CLASSIFIER_USER_PROMPT_TEMPLATE`, `ACCOUNT_CLASSIFIER_PROMPT_VERSION`. `analysis/src/llm/schemas.py` lost `ACCOUNT_CLASSIFIER_SCHEMA`.
- `analysis/src/common/settings.py` lost `account_classifier_llm_enabled`. `analysis/src/scheduler/job_runner.py:run_account_classification` is one-line shorter (no `classify_with_llm` call), and the constructor passes only `db_path` to `AccountClassifier`.
- `analysis/tests/test_account_classifier.py` lost `TestLLMClassifier` and the `_FakeLLMClient` helper. The curated-loader, parse-format, and narrative-aggregator tier-join tests stay — those are still load-bearing for the curated path.

### Tests

- `analysis/tests/test_context_seeds.py` covers: frontmatter parse + skip on malformed file, word-boundary case-insensitive match (including the "vac" vs "vacuum" collision case), MAX_SEEDS cap, format-block citation metadata, analyzer + claim-extractor inject the block when matched and skip it when not.

## Why

### The seeding side

Outputs were shallow on topics with established factual baselines. Sentiment and claim extraction were running over claims about vaccines, climate change, and economic data with no ambient grounding — the model had to reason from prompt text alone. Two failure modes that hit on real docs in the dev DB:

- A claim like "vaccines cause autism" came out of the extractor at `confidence ≈ 0.85` because the rubric only cares whether a verbatim evidence span backs the claim — not whether the claim is contested by the public-health consensus.
- Sentiment around inflation coverage drifted because the model had no anchor for what "high inflation" means quantitatively, so heated rhetoric and measured reporting at similar topic-distance scored similarly.

Adding factual baselines does not change scoring rubrics (the prompts are explicit about that) but gives the model the same grounding a knowledgeable human reader would bring. This is the "seed → ground the model, don't bias it" line from the task brief — sources speak for themselves.

### The classifier side

When officials-tier identification moved to `verified_officials.yaml` + `entity_registry.resolve_entity()` (walkthrough 058), the LLM classifier became dead plumbing for officials. It was still doing real work for non-curated authors — speculatively classifying high-volume accounts as `affiliated` or `general_public` — but those classifications were:

- Not used by any aggregator surface in the current UI (entity routing flows through `entity_registry`, not `account_profiles.tier`).
- Cost-bearing and hard to audit (per-author confidence and reasoning lived in `account_profiles.reasoning`, not the structured `ai_outputs` audit table).
- Redundant with the curated `known_political_x_accounts.yaml` (547 entries) for the elected-official cohort.

Per the project-wide "MVP — no shims, delete dead code as you go" stance, the classifier was removed in the same change as the seeding work because both touch the LLM-prompt registry and `prompts.py` was already changing. Officials are now identified by being present in `verified_officials.yaml`; the curated YAML covers the wider elected/affiliated set; everything else is `general_public` by default at the aggregator layer.

## Follow-ups

- Add reference entries for vaccine-specific subtopics (mRNA mechanism, thimerosal history) if claim-extraction continues to surface those subclaims at high confidence.
- Voting records and per-official statements are deliberately out of scope for the seed registry — those would need their own subsystem with primary-source pulls, see `docs/proposals/fact-check-agent.md`.
- Migration 019 drops `account_profiles.confidence` and `account_profiles.reasoning` (the LLM-classifier-only columns) in the same change. No remaining query references them.
