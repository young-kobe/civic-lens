# Civic Lens Database Schema Reference

> **Version**: 2.0
> **Last Updated**: 2026-07-09
> **Source of truth**: `data/migrations/001-021`. This document is regenerated
> from the live schema a scratch DB reconstructs by applying every migration in
> order; when it drifts, rebuild and re-diff rather than hand-editing.

---

## Overview

Civic Lens uses SQLite with a single unified schema shared by the ingestion
(Go) and analysis (Python) layers. Both open the DB with `PRAGMA
foreign_keys=ON` (Go via the DSN, Python via each connection helper — audit
D-5), so the foreign keys below are enforced on both sides.

Migrations are applied by the Go runner (`ingest/internal/storage/db/db.go`),
which wraps each migration file and its `schema_version` INSERT in one
transaction unless the file manages its own (`BEGIN ... COMMIT` table
rebuilds). `schema_version` records versions 1-21.

Sixteen live tables:

- **Ingestion**: `pages`, `articles_raw`, `reddit_posts_raw`, `x_posts_raw`,
  `x_users_raw`
- **Normalized corpus**: `docs`
- **Analysis**: `ai_outputs`, `ai_output_evals`, `prompt_versions`,
  `narratives`, `narrative_docs`, `narrative_citations`, `account_profiles`,
  `author_bot_scores`
- **Ops**: `x_api_budget`, `schema_version`

Tables dropped by migration 005 (`reddit_comments_raw`, `clusters`,
`cluster_assignments`) are gone and no longer documented.

---

## Ingestion Tables

### pages (Frontier)

Crawl-state machine for the web crawler. `state`: 0=QUEUED, 1=INFLIGHT,
2=DONE, 3=FAILED (`CHECK(state IN (0,1,2,3))`, migration 013). INFLIGHT rows
are reset to QUEUED on startup.

| Column | Type | Notes |
| --- | --- | --- |
| url_canon | TEXT | PRIMARY KEY |
| url_raw | TEXT | NOT NULL |
| domain | TEXT | NOT NULL |
| state | INTEGER | NOT NULL DEFAULT 0, `CHECK(state IN (0,1,2,3))` |
| priority | INTEGER | NOT NULL DEFAULT 0 |
| retries | INTEGER | NOT NULL DEFAULT 0 |
| next_fetch_at | INTEGER | NOT NULL DEFAULT 0 |
| inflight_at | INTEGER | NOT NULL DEFAULT 0 |
| http_status | INTEGER | |
| content_sha256 | TEXT | |
| etag | TEXT | |
| last_modified | TEXT | |
| last_error | TEXT | |

Indexes: `idx_pages_state_next_fetch(state, next_fetch_at)`,
`idx_pages_domain(domain)`.

### articles_raw

Extracted article metadata. FK `url_canon` → `pages(url_canon)`.

| Column | Type | Notes |
| --- | --- | --- |
| url_canon | TEXT | PRIMARY KEY, FK → pages(url_canon) |
| domain | TEXT | |
| fetched_at | INTEGER | NOT NULL |
| published_at | INTEGER | |
| title | TEXT | |
| raw_hash | TEXT | NOT NULL |
| extraction_version | TEXT | NOT NULL |

### reddit_posts_raw

| Column | Type | Notes |
| --- | --- | --- |
| fullname | TEXT | PRIMARY KEY |
| subreddit | TEXT | |
| created_utc | INTEGER | |
| fetched_at | INTEGER | |
| title | TEXT | |
| body | TEXT | |
| score | INTEGER | |
| num_comments | INTEGER | |
| raw_hash | TEXT | NOT NULL |
| extraction_version | TEXT | NOT NULL |

### x_posts_raw

Raw X (Twitter) posts. `is_official_tier` (migration 018) is a provenance
flag set to 1 when the post arrived via the verified-officials timeline pull;
the analysis tier-routing path reads it (audit D-4).

| Column | Type | Notes |
| --- | --- | --- |
| tweet_id | TEXT | PRIMARY KEY |
| author_id | TEXT | NOT NULL |
| conversation_id | TEXT | |
| created_at | INTEGER | NOT NULL |
| fetched_at | INTEGER | NOT NULL |
| text | TEXT | NOT NULL |
| lang | TEXT | |
| retweet_count / reply_count / like_count / quote_count | INTEGER | DEFAULT 0 |
| place_id | TEXT | |
| place_country_code | TEXT | from includes.places expansion |
| place_full_name | TEXT | |
| context_annotations_json | TEXT | JSON array |
| in_reply_to_user_id | TEXT | |
| referenced_tweet_id | TEXT | |
| referenced_tweet_type | TEXT | 'replied_to' \| 'quoted' \| 'retweeted' |
| raw_hash | TEXT | NOT NULL |
| extraction_version | TEXT | NOT NULL |
| is_official_tier | INTEGER | NOT NULL DEFAULT 0 |

Indexes: `idx_x_posts_author(author_id)`, `idx_x_posts_created(created_at)`,
`idx_x_posts_country(place_country_code)`,
`idx_x_posts_raw_official_tier(is_official_tier) WHERE is_official_tier = 1`.

### x_users_raw

| Column | Type | Notes |
| --- | --- | --- |
| user_id | TEXT | PRIMARY KEY |
| username | TEXT | NOT NULL |
| name | TEXT | |
| location | TEXT | self-declared, freeform |
| description | TEXT | |
| created_at | INTEGER | |
| followers_count / following_count / tweet_count / listed_count | INTEGER | DEFAULT 0 |
| verified | INTEGER | DEFAULT 0 |
| verified_type | TEXT | 'blue' \| 'business' \| 'government' |
| profile_image_url | TEXT | |
| protected | INTEGER | DEFAULT 0 |
| fetched_at | INTEGER | NOT NULL |
| raw_hash | TEXT | NOT NULL |

Indexes: `idx_x_users_username(username)`, `idx_x_users_created(created_at)`.

---

## Normalized Corpus

### docs

Normalized documents produced by the Python ETL from the raw tables, filtered
to ~30 days of US-political content. The parent of every analysis FK.

`source_type` is constrained to `CHECK(source_type IN ('news', 'reddit',
'reddit_post', 'reddit_comment', 'x_post'))` (migration 003 added `x_post`).
`place_country_code` and `fetched_at` were dropped by migration 021 (audit
D-10, D-13): the country signal now lives only inside `metadata_json`, where
`bot.py` reads it.

| Column | Type | Notes |
| --- | --- | --- |
| doc_id | INTEGER | PRIMARY KEY AUTOINCREMENT |
| source_type | TEXT | NOT NULL, `CHECK` (see above) |
| ident | TEXT | UNIQUE NOT NULL |
| domain_or_subreddit | TEXT | |
| published_at | INTEGER | |
| title | TEXT | |
| text | TEXT | |
| raw_hash | TEXT | NOT NULL |
| metadata_json | TEXT | per-source extras (e.g. X `place_country_code`, author metadata) |
| etl_version | TEXT | ETL logic version stamp (migration 020) |

Indexes: `idx_docs_published_at(published_at)`, `idx_docs_ident(ident)`.

---

## Analysis Tables

### ai_outputs

One row per (doc, task) model output. Every row carries `confidence`,
`model_id`, and `prompt_version` (AI-output contract). `inference_method`
(migration 012) is `CHECK(inference_method IS NULL OR inference_method IN
('llm', 'heuristic', 'deterministic'))`; `'deterministic'` rows are
pre-exclusion markers dropped from bot denominators. FK `doc_id` →
`docs(doc_id)`.

| Column | Type | Notes |
| --- | --- | --- |
| output_id | INTEGER | PRIMARY KEY AUTOINCREMENT |
| doc_id | INTEGER | NOT NULL, FK → docs(doc_id) |
| model_id | TEXT | |
| prompt_version | TEXT | |
| task_type | TEXT | NOT NULL (e.g. sentiment, favorability, bot_detection, propaganda, claim) |
| output_json | TEXT | NOT NULL |
| confidence | REAL | |
| created_at | INTEGER | |
| inference_method | TEXT | `CHECK` (see above) |

Indexes: `idx_ai_outputs_doc_task(doc_id, task_type)`,
`idx_ai_outputs_method(inference_method)`.

### ai_output_evals

Human-in-loop review markers (golden set + correctness). FK `ai_output_id` →
`ai_outputs(output_id)` (UNIQUE), FK `doc_id` → `docs(doc_id)`.

| Column | Type | Notes |
| --- | --- | --- |
| eval_id | INTEGER | PRIMARY KEY AUTOINCREMENT |
| ai_output_id | INTEGER | NOT NULL UNIQUE, FK → ai_outputs(output_id) |
| doc_id | INTEGER | NOT NULL, FK → docs(doc_id) |
| task_type | TEXT | NOT NULL |
| human_label | TEXT | |
| human_confidence | REAL | |
| is_correct | INTEGER | 1 correct, 0 incorrect, NULL unreviewed |
| is_golden | INTEGER | NOT NULL DEFAULT 0 |
| reviewer_id | TEXT | |
| reviewed_at | INTEGER | NOT NULL |
| notes | TEXT | |

Indexes: `idx_ai_output_evals_doc(doc_id)`,
`idx_ai_output_evals_golden(is_golden, task_type)`.

### prompt_versions

Registry of prompt versions used by LLM tasks.

| Column | Type | Notes |
| --- | --- | --- |
| prompt_version | TEXT | PRIMARY KEY |
| task_type | TEXT | NOT NULL |
| system_prompt | TEXT | NOT NULL |
| user_prompt_template | TEXT | |
| created_at | INTEGER | NOT NULL |
| notes | TEXT | |

Index: `idx_prompt_versions_task(task_type)`.

### narratives

Clustered recurring claims. `first_seen_*` reference the earliest doc WE
ingested. Embedding/clustering columns were added by migrations 008/015. FK
`first_seen_doc_id` → `docs(doc_id)`.

| Column | Type | Notes |
| --- | --- | --- |
| narrative_id | INTEGER | PRIMARY KEY AUTOINCREMENT |
| name | TEXT | NOT NULL |
| description | TEXT | |
| first_seen_at | INTEGER | |
| first_seen_doc_id | INTEGER | FK → docs(doc_id) |
| created_at | INTEGER | NOT NULL |
| updated_at | INTEGER | |
| anchor_embedding_json | TEXT | |
| clustering_mode | TEXT | jaccard \| embedding |
| clustering_threshold | REAL | |
| embedding_model | TEXT | |

Index: `idx_narratives_first_seen(first_seen_at)`.

### narrative_docs

Doc-to-narrative assignments. `UNIQUE(narrative_id, doc_id)`. FKs → narratives,
docs.

| Column | Type | Notes |
| --- | --- | --- |
| assignment_id | INTEGER | PRIMARY KEY AUTOINCREMENT |
| narrative_id | INTEGER | NOT NULL, FK → narratives(narrative_id) |
| doc_id | INTEGER | NOT NULL, FK → docs(doc_id) |
| discovered_at | INTEGER | NOT NULL |
| confidence | REAL | |

Indexes: `idx_narrative_docs_narrative`, `idx_narrative_docs_doc`,
`idx_narrative_docs_discovered`.

### narrative_citations

Deterministic citation edges between ingested docs (migration 016 dropped the
`repost` link type). `link_type` is `CHECK(link_type IN ('url_citation',
'quote', 'reply', 'retweet'))`; a row must have a target
(`CHECK((target_doc_id IS NOT NULL) OR (target_url IS NOT NULL))`). FKs
`source_doc_id`, `target_doc_id` → `docs(doc_id)`.

| Column | Type | Notes |
| --- | --- | --- |
| citation_id | INTEGER | PRIMARY KEY AUTOINCREMENT |
| source_doc_id | INTEGER | NOT NULL, FK → docs(doc_id) |
| target_doc_id | INTEGER | FK → docs(doc_id) |
| target_url | TEXT | |
| link_type | TEXT | NOT NULL, `CHECK` (see above) |
| discovered_at | INTEGER | NOT NULL |

Indexes: `idx_narrative_citations_source`, `idx_narrative_citations_target`,
`idx_narrative_citations_target_url`.

### account_profiles

Per-account tier metadata loaded from the curated YAML registries. Migration
019 removed the LLM-classifier columns; the faction columns (migration 011,
nullable) are populated by the curated loader. `UNIQUE(platform, author_id)`.

- `platform`: `CHECK(platform IN ('x', 'reddit'))`
- `tier`: `CHECK(tier IN ('elected_official', 'affiliated', 'general_public'))`
- `classification_method`: `CHECK(classification_method IN ('curated_list', 'llm'))`

| Column | Type | Notes |
| --- | --- | --- |
| profile_id | INTEGER | PRIMARY KEY AUTOINCREMENT |
| platform | TEXT | NOT NULL, `CHECK` |
| author_id | TEXT | NOT NULL |
| display_name | TEXT | |
| tier | TEXT | NOT NULL, `CHECK` |
| classification_method | TEXT | NOT NULL, `CHECK` |
| classified_at | INTEGER | NOT NULL |
| notes | TEXT | |
| full_name / party / branch / chamber / state_or_district / office_title / account_type | TEXT | nullable, curated |

Indexes: `idx_account_profiles_tier`,
`idx_account_profiles_platform_author(platform, author_id)`,
`idx_account_profiles_party`, `idx_account_profiles_branch`.

### author_bot_scores

Per-author bot aggregates (migration 014). Composite PK `(platform,
author_id)`; `platform` is `CHECK(platform IN ('x', 'reddit'))`.

| Column | Type | Notes |
| --- | --- | --- |
| platform | TEXT | NOT NULL, `CHECK`, PK part |
| author_id | TEXT | NOT NULL, PK part |
| score | REAL | NOT NULL, mean post-level aggregated score |
| variance | REAL | cross-post variance |
| sample_count | INTEGER | NOT NULL |
| bot_post_count | INTEGER | NOT NULL DEFAULT 0 |
| suspicious_post_count | INTEGER | NOT NULL DEFAULT 0 |
| llm_text_likelihood_mean | REAL | |
| stylometric_features_json | TEXT | |
| updated_at | INTEGER | NOT NULL |

Indexes: `idx_author_bot_scores_score`, `idx_author_bot_scores_bot_count`.

---

## Ops Tables

### x_api_budget

Persistent per-month X API spend tracker (migration 017). One row per UTC
calendar month.

| Column | Type | Notes |
| --- | --- | --- |
| month_key | TEXT | PRIMARY KEY, 'YYYY-MM' UTC |
| post_count | INTEGER | NOT NULL DEFAULT 0 |
| user_count | INTEGER | NOT NULL DEFAULT 0 |
| request_count | INTEGER | NOT NULL DEFAULT 0 |
| estimated_cents | INTEGER | NOT NULL DEFAULT 0 |
| last_updated | INTEGER | NOT NULL DEFAULT 0, unix epoch |

### schema_version

| Column | Type | Notes |
| --- | --- | --- |
| version | INTEGER | PRIMARY KEY |
| applied_at | INTEGER | NOT NULL |

Populated 1-21. Migration 004's row is backfilled by migration 021 (audit
D-12), so no version is skipped.
