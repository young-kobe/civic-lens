-- 0001_north_star.sql
-- Civic Lens Postgres redesign — greenfield DDL for the entire target schema.
--
-- This is Phase 1 of the Postgres redesign (see docs/todos/pg-redesign.md and
-- the approved plan). It is a single script, no PG migration history to
-- replay: CREATE SCHEMA, CREATE TYPE, CREATE TABLE in dependency order,
-- six schemas: raw, corpus, analysis, serving, ops, archive.
--
-- Conventions used throughout:
--   - All surrogate keys are BIGINT GENERATED ALWAYS AS IDENTITY, except:
--       * raw.* natural-key tables (url_canon / fullname / tweet_id / user_id
--         / month_key), which keep their real-world identifier as PK — raw
--         is a near-1:1 port of the ingestion frontier and must stay
--         byte-faithful to what the crawler/fetchers already key on.
--       * archive.* tables, which preserve the imported SQLite integer IDs
--         verbatim (traceability to the cold artifact depends on the old
--         numbers surviving unchanged) — see the archive schema header.
--   - All timestamps are TIMESTAMPTZ (SQLite epoch integers convert via
--     to_timestamp() at import time — see tools/migrate_sqlite_to_pg.py,
--     Phase 3).
--   - JSONB appears in exactly three places: analysis.runs.raw_response,
--     ops.pipeline_runs.stage_summary, and archive.* (verbatim import of
--     old JSON columns). Every other table uses typed columns.
--   - Enums are used wherever the plan specifies a closed vocabulary. This
--     is a stricter contract than the old SQLite CHECK-constraint style,
--     and deliberate: pgAdmin browsability + real referential guarantees
--     are the whole point of the redesign (Kobe wants to work the data
--     hands-on).
--
-- Judgment calls made where the plan was loose are called out inline with
-- a "-- JUDGMENT CALL:" comment at the point of the decision, and summarized
-- in the Phase 1 audit-trail entry / task report.

-- =============================================================================
-- Schemas
-- =============================================================================

CREATE SCHEMA raw;      -- Go ingestor only. Frontier + verbatim source capture.
CREATE SCHEMA corpus;   -- Python ETL only (etl/registry_sync.py, authors.py, documents.py).
CREATE SCHEMA analysis; -- Python engine + results/store.py only. One writer: results/store.py.
CREATE SCHEMA serving;  -- Python serving/ rollup builders write; FastAPI reads only.
CREATE SCHEMA IF NOT EXISTS ops; -- Go (x_api_budget) + Python scheduler (task_queue, pipeline_runs) write.
                        -- IF NOT EXISTS: the Go migration runner bootstraps this schema
                        -- (plus ops.schema_migrations, below) before applying any migration,
                        -- so it can record that this very file has been applied — see
                        -- bootstrapSchemaMigrations in ingest/internal/storage/db/db.go.
                        -- Every other object in this file still fails loud on double-apply.
CREATE SCHEMA archive;  -- One-time import (tools/migrate_sqlite_to_pg.py --archive). Read-only
                        -- by convention thereafter — see the archive section footer.

COMMENT ON SCHEMA raw IS 'Ingestion capture layer, written only by the Go crawler/fetchers. Near-1:1 port of the old SQLite raw tables — deliberately not redesigned.';
COMMENT ON SCHEMA corpus IS 'Normalized corpus: documents, authors, and the YAML entity registry materialized. Written only by analysis/src/etl/.';
COMMENT ON SCHEMA analysis IS 'LLM/deterministic analysis runs and their typed per-task results. Written only by analysis/src/results/store.py.';
COMMENT ON SCHEMA serving IS 'Precomputed rollups the API reads. Rebuilt per window inside one transaction (DELETE+INSERT) so the API never reads a half-built window.';
COMMENT ON SCHEMA ops IS 'Pipeline machinery: work queue, run provenance, X API budget, migration ledger.';
COMMENT ON SCHEMA archive IS 'Verbatim one-time import of the old SQLite derived data, kept "just to have". No FKs, no new code reads or writes it.';

-- =============================================================================
-- raw — capture layer (Go-only writer)
-- =============================================================================

CREATE TYPE raw.page_state AS ENUM ('queued', 'inflight', 'done', 'failed');

CREATE TABLE raw.pages (
    url_canon TEXT PRIMARY KEY,
    url_raw TEXT NOT NULL,
    domain TEXT NOT NULL,
    state raw.page_state NOT NULL DEFAULT 'queued',
    priority INTEGER NOT NULL DEFAULT 0,
    retries INTEGER NOT NULL DEFAULT 0,
    next_fetch_at TIMESTAMPTZ NOT NULL DEFAULT to_timestamp(0),
    inflight_at TIMESTAMPTZ NOT NULL DEFAULT to_timestamp(0),
    http_status INTEGER,
    content_sha256 TEXT,
    etag TEXT,
    last_modified TEXT,
    last_error TEXT
);
COMMENT ON TABLE raw.pages IS 'Crawl frontier state machine: queued -> inflight -> done|failed. inflight rows are reset to queued on startup (crash-safety).';
COMMENT ON COLUMN raw.pages.state IS 'Frontier state; inflight is reset to queued at process start, never persisted across a crash.';

CREATE INDEX idx_pages_state_next_fetch ON raw.pages (state, next_fetch_at);
CREATE INDEX idx_pages_domain ON raw.pages (domain);

CREATE TABLE raw.articles (
    url_canon TEXT PRIMARY KEY REFERENCES raw.pages (url_canon),
    domain TEXT,
    fetched_at TIMESTAMPTZ NOT NULL,
    published_at TIMESTAMPTZ,
    title TEXT,
    raw_hash TEXT NOT NULL,
    extraction_version TEXT NOT NULL
);
COMMENT ON COLUMN raw.articles.raw_hash IS 'Content-addressed key into data/raw/sha256/<hash> on disk.';

CREATE TABLE raw.reddit_posts (
    fullname TEXT PRIMARY KEY,
    subreddit TEXT,
    created_utc TIMESTAMPTZ,
    fetched_at TIMESTAMPTZ,
    title TEXT,
    body TEXT,
    score INTEGER,
    num_comments INTEGER,
    raw_hash TEXT NOT NULL,
    extraction_version TEXT NOT NULL
);

CREATE TABLE raw.x_posts (
    tweet_id TEXT PRIMARY KEY,
    author_id TEXT NOT NULL,
    conversation_id TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    fetched_at TIMESTAMPTZ NOT NULL,
    text TEXT NOT NULL,
    lang TEXT,
    retweet_count INTEGER NOT NULL DEFAULT 0,
    reply_count INTEGER NOT NULL DEFAULT 0,
    like_count INTEGER NOT NULL DEFAULT 0,
    quote_count INTEGER NOT NULL DEFAULT 0,
    place_id TEXT,
    place_country_code TEXT,
    place_full_name TEXT,
    context_annotations JSONB,
    in_reply_to_user_id TEXT,
    referenced_tweet_id TEXT,
    referenced_tweet_type TEXT,
    raw_hash TEXT NOT NULL,
    extraction_version TEXT NOT NULL,
    is_official_tier BOOLEAN NOT NULL DEFAULT false
);
COMMENT ON COLUMN raw.x_posts.author_id IS 'Deliberately NOT a FK to raw.x_users — capture must never drop a post because its author row has not (yet, or ever) been fetched.';
COMMENT ON COLUMN raw.x_posts.is_official_tier IS 'Set when the post arrived via the verified-officials timeline pull; the analysis tier-routing path reads it.';

CREATE INDEX idx_x_posts_author ON raw.x_posts (author_id);
CREATE INDEX idx_x_posts_created ON raw.x_posts (created_at);
CREATE INDEX idx_x_posts_country ON raw.x_posts (place_country_code);
CREATE INDEX idx_x_posts_official_tier ON raw.x_posts (is_official_tier) WHERE is_official_tier;

CREATE TABLE raw.x_users (
    user_id TEXT PRIMARY KEY,
    username TEXT NOT NULL,
    name TEXT,
    location TEXT,
    description TEXT,
    created_at TIMESTAMPTZ,
    followers_count INTEGER NOT NULL DEFAULT 0,
    following_count INTEGER NOT NULL DEFAULT 0,
    tweet_count INTEGER NOT NULL DEFAULT 0,
    listed_count INTEGER NOT NULL DEFAULT 0,
    verified BOOLEAN NOT NULL DEFAULT false,
    verified_type TEXT,
    profile_image_url TEXT,
    protected BOOLEAN NOT NULL DEFAULT false,
    fetched_at TIMESTAMPTZ NOT NULL,
    raw_hash TEXT NOT NULL
);

CREATE INDEX idx_x_users_username ON raw.x_users (username);
CREATE INDEX idx_x_users_created ON raw.x_users (created_at);

-- =============================================================================
-- corpus — normalized corpus + entity registry (Python ETL writer)
-- =============================================================================

-- The single source of truth for curated political lean. Owner decision
-- (supersedes the plan's original two-column party/lean sketch): ONE flat
-- lean convention across the entire schema, one enum, one column name
-- ("lean") everywhere it appears. No other table stores curated
-- affiliation; every derived lean (analysis.author_leans,
-- analysis.narrative_leans) is computed once and joined, never re-derived.
-- Political lean is NEVER fed into an LLM prompt (bias/priming risk) — every
-- engine that reads corpus.entities for prompt context must exclude this
-- column and lean_source.
CREATE TYPE corpus.political_lean AS ENUM (
    'democrat', 'republican', 'independent', 'mixed', 'unknown'
);

CREATE TYPE corpus.source_type AS ENUM ('news', 'reddit_post', 'x_post');
CREATE TYPE corpus.entity_kind AS ENUM ('official', 'collective', 'outlet', 'subreddit');
CREATE TYPE corpus.author_tier AS ENUM ('elected_official', 'affiliated', 'general_public');
CREATE TYPE corpus.classification_method AS ENUM ('curated_list', 'llm');
CREATE TYPE corpus.platform AS ENUM ('x', 'reddit');

CREATE TABLE corpus.entities (
    entity_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    entity_key TEXT NOT NULL UNIQUE,
    kind corpus.entity_kind NOT NULL,
    display_name TEXT NOT NULL,
    blurb TEXT,
    -- JUDGMENT CALL: the plan specifies a lean column but does not
    -- enumerate the rest of the curated registry. These are the curated
    -- fields that exist in data/verified_officials.yaml,
    -- data/news_outlets.yaml, and data/major_subreddits.yaml today and
    -- have nowhere else to live now that the registry is materialized
    -- into the DB. Nullable and only populated for the kind they apply to
    -- (role_title/term_start/source_citation for officials; owner/
    -- source_citation for outlets), matching the old account_profiles
    -- convention of mixed nullable columns across tiers.
    role_title TEXT,
    term_start DATE,
    owner TEXT,
    source_citation TEXT,
    -- ONE flat lean convention (owner decision, supersedes the plan's
    -- separate party/lean sketch): for officials/collectives, party
    -- membership IS the lean value (mapped losslessly, e.g. R -> republican,
    -- D -> democrat, I -> independent); for outlets/subreddits, the curated
    -- YAML lean scale flattens deterministically (see
    -- analysis/src/common/registry.py's flattening constants). No separate
    -- party column anywhere in the schema — this is the only stored
    -- affiliation value.
    lean corpus.political_lean NOT NULL DEFAULT 'unknown',
    -- Provenance/audit only, never a join axis: the ORIGINAL pre-flattening
    -- YAML string (e.g. "center-left", "R", "independent-dem"), preserved so
    -- the flattening is auditable/reversible. Distinct from source_citation
    -- above, which holds the curation citation (bio_source / AllSides
    -- rating text) — lean_source holds the raw classification value itself.
    lean_source TEXT,
    active BOOLEAN NOT NULL DEFAULT true,
    -- true for the 3 hand-edited registries; false for the ~549 officials
    -- promoted wholesale from known_political_x_accounts.yaml. See
    -- analysis/src/etl/registry_sync.py.
    editorial BOOLEAN NOT NULL DEFAULT false,
    synced_at TIMESTAMPTZ NOT NULL
);
COMMENT ON TABLE corpus.entities IS 'YAML registry (verified_officials/news_outlets/major_subreddits/known_political_x_accounts) materialized. YAML in git remains source of truth; ETL sync upserts and flips active=false for removed keys — never DELETE, so historical FKs always resolve.';
COMMENT ON COLUMN corpus.entities.entity_key IS 'Stable slug = the YAML key (domain / handle / subreddit name).';
COMMENT ON COLUMN corpus.entities.lean IS 'THE single source of truth for curated political lean (officials'' party AND outlets''/subreddits'' media lean, flattened onto the same 5-value corpus.political_lean enum). No other table stores curated affiliation; every derived lean (analysis.author_leans, analysis.narrative_leans) and every serving rollup JOINs this column rather than re-deriving it. NEVER fed into an LLM prompt (bias/priming risk).';
COMMENT ON COLUMN corpus.entities.lean_source IS 'Display/audit only, never a join axis: the original pre-flattening YAML string (outlet partisan_lean / subreddit tilt / official party code) that registry_sync.py flattened into `lean`. Preserves the richer curated vocabulary the owner chose to collapse.';
COMMENT ON COLUMN corpus.entities.active IS 'false = removed from YAML; row is kept (never DELETEd) so historical FKs keep resolving.';
COMMENT ON COLUMN corpus.entities.editorial IS 'true = one of the 3 hand-edited registries. false = promoted from known_political_x_accounts.yaml. Both kinds carry a real lean/entity_id; UI dropdowns/rollups filter on this.';

CREATE INDEX idx_entities_editorial ON corpus.entities (kind) WHERE editorial;
COMMENT ON INDEX corpus.idx_entities_editorial IS 'Common filter: editorial entities by kind.';

CREATE TABLE corpus.entity_aliases (
    entity_id BIGINT NOT NULL REFERENCES corpus.entities (entity_id),
    alias TEXT NOT NULL,
    PRIMARY KEY (entity_id, alias)
);
COMMENT ON TABLE corpus.entity_aliases IS 'Alternate matching keys, e.g. news_outlets.yaml also_domains.';

CREATE TABLE corpus.authors (
    author_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    platform corpus.platform NOT NULL,
    platform_author_id TEXT NOT NULL,
    handle TEXT,
    display_name TEXT,
    description TEXT,
    location TEXT,
    profile_image_url TEXT,
    verified BOOLEAN,
    verified_type TEXT,
    followers_count INTEGER,
    following_count INTEGER,
    account_created_at TIMESTAMPTZ,
    last_synced_at TIMESTAMPTZ,
    UNIQUE (platform, platform_author_id)
);
COMMENT ON TABLE corpus.authors IS 'Unified author identity across x_users_raw/reddit metadata/account_profiles. Denormalized latest profile snapshot — not a history table.';

CREATE TABLE corpus.author_profiles (
    author_profile_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    author_id BIGINT NOT NULL UNIQUE REFERENCES corpus.authors (author_id),
    tier corpus.author_tier NOT NULL,
    method corpus.classification_method NOT NULL,
    entity_id BIGINT REFERENCES corpus.entities (entity_id),
    classified_at TIMESTAMPTZ NOT NULL,
    notes TEXT
);
COMMENT ON TABLE corpus.author_profiles IS 'Replaces account_profiles. entity_id links the account to its curated registry entity when one exists (e.g. a senator''s personal handle -> the "sen-x" entity); NULL for general_public accounts with no registry match. Deliberately has NO party/lean column of its own: an official''s lean is read by joining entity_id -> corpus.entities.lean. Storing it here too would duplicate the single source of truth the owner decision requires.';

CREATE TABLE corpus.documents (
    doc_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_type corpus.source_type NOT NULL,
    natural_key TEXT NOT NULL,
    domain_or_subreddit TEXT,
    author_id BIGINT REFERENCES corpus.authors (author_id),
    published_at TIMESTAMPTZ NOT NULL,
    title TEXT,
    body TEXT NOT NULL,
    source_url TEXT NOT NULL,
    raw_hash TEXT NOT NULL,
    etl_version TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (source_type, natural_key)
);
COMMENT ON TABLE corpus.documents IS 'Parent of every analysis FK. natural_key is url_canon | fullname | tweet_id depending on source_type — the ETL idempotency key.';
COMMENT ON COLUMN corpus.documents.natural_key IS 'ETL idempotency key: url_canon (news), fullname (reddit_post), tweet_id (x_post).';
COMMENT ON COLUMN corpus.documents.source_url IS 'NOT NULL — invariant C1 (every surfaced doc must carry a source link).';
COMMENT ON COLUMN corpus.documents.etl_version IS 'ETL logic version stamp, e.g. "pg-1".';

CREATE INDEX idx_documents_published_at ON corpus.documents (published_at);
CREATE INDEX idx_documents_author ON corpus.documents (author_id);
CREATE INDEX idx_documents_domain_or_subreddit ON corpus.documents (domain_or_subreddit);

CREATE TABLE corpus.news_articles (
    doc_id BIGINT PRIMARY KEY REFERENCES corpus.documents (doc_id),
    url_canon TEXT NOT NULL UNIQUE REFERENCES raw.articles (url_canon),
    domain TEXT,
    extraction_version TEXT,
    outlet_entity_id BIGINT REFERENCES corpus.entities (entity_id)
);
COMMENT ON COLUMN corpus.news_articles.outlet_entity_id IS 'Nullable FK to corpus.entities (kind=''outlet''), resolved at ETL time (analysis/src/etl/documents.py) by canonicalizing `domain` the same way registry_sync.py canonicalizes news_outlets.yaml domains/also_domains. NULL when the outlet is not (yet) in the registry -- never blocks a doc; a later registry_sync run that adds/reactivates the outlet is backfilled on the next documents.py run.';

CREATE INDEX idx_news_articles_outlet_entity ON corpus.news_articles (outlet_entity_id);

CREATE TABLE corpus.reddit_posts (
    doc_id BIGINT PRIMARY KEY REFERENCES corpus.documents (doc_id),
    fullname TEXT NOT NULL UNIQUE REFERENCES raw.reddit_posts (fullname),
    subreddit TEXT,
    score INTEGER,
    num_comments INTEGER,
    subreddit_entity_id BIGINT REFERENCES corpus.entities (entity_id)
);
COMMENT ON COLUMN corpus.reddit_posts.subreddit_entity_id IS 'Nullable FK to corpus.entities (kind=''subreddit''), resolved at ETL time the same way as news_articles.outlet_entity_id (see that column''s comment) -- canonicalized via registry.canonicalize_subreddit, NULL when unmatched, backfilled on a later run once the subreddit is registered.';

CREATE INDEX idx_reddit_posts_subreddit_entity ON corpus.reddit_posts (subreddit_entity_id);

CREATE TABLE corpus.x_posts (
    doc_id BIGINT PRIMARY KEY REFERENCES corpus.documents (doc_id),
    tweet_id TEXT NOT NULL UNIQUE REFERENCES raw.x_posts (tweet_id),
    conversation_id TEXT,
    lang TEXT,
    place_country_code TEXT,
    retweet_count INTEGER,
    reply_count INTEGER,
    like_count INTEGER,
    quote_count INTEGER,
    is_official_tier BOOLEAN
);
COMMENT ON TABLE corpus.x_posts IS 'The convention-only docs.ident join to x_posts_raw becomes this enforced FK on tweet_id.';

-- =============================================================================
-- analysis — runs + typed per-task results (analysis/src/results/store.py only writer)
-- =============================================================================

CREATE TYPE analysis.task AS ENUM (
    'bot', 'text', 'targets', 'propaganda', 'claims', 'citations', 'account_tier'
);
CREATE TYPE analysis.run_status AS ENUM ('pending', 'running', 'done', 'failed');
CREATE TYPE analysis.inference_method AS ENUM ('llm', 'deterministic', 'hybrid');
CREATE TYPE analysis.sentiment_label AS ENUM ('positive', 'negative', 'neutral', 'mixed');
CREATE TYPE analysis.favorability_label AS ENUM ('favorable', 'unfavorable', 'neutral', 'mixed');
CREATE TYPE analysis.bot_label AS ENUM ('bot', 'suspicious', 'human', 'unknown');
CREATE TYPE analysis.propaganda_technique AS ENUM (
    'loaded_language', 'name_calling', 'ad_hominem', 'appeal_to_fear',
    'whataboutism', 'doubt_casting'
);
CREATE TYPE analysis.link_type AS ENUM ('url_citation', 'quote', 'reply', 'retweet');
CREATE TYPE analysis.verdict AS ENUM ('correct', 'incorrect', 'uncertain');

CREATE TABLE analysis.prompt_versions (
    prompt_version_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    prompt_version TEXT NOT NULL UNIQUE,
    task analysis.task NOT NULL,
    system_prompt TEXT NOT NULL,
    user_prompt_template TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    notes TEXT
);
COMMENT ON COLUMN analysis.prompt_versions.prompt_version IS 'Human-facing label, e.g. "sentiment_v3" — the value engine/prompts.py bumps.';

CREATE TABLE analysis.runs (
    run_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    task analysis.task NOT NULL,
    doc_id BIGINT REFERENCES corpus.documents (doc_id),
    author_id BIGINT REFERENCES corpus.authors (author_id),
    status analysis.run_status NOT NULL DEFAULT 'pending',
    model_id TEXT NOT NULL,
    prompt_version_id BIGINT REFERENCES analysis.prompt_versions (prompt_version_id),
    inference_method analysis.inference_method NOT NULL,
    confidence REAL,
    is_current BOOLEAN NOT NULL DEFAULT true,
    raw_response JSONB,
    started_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT runs_doc_xor_author CHECK ((doc_id IS NULL) <> (author_id IS NULL))
);
COMMENT ON TABLE analysis.runs IS 'One row per analysis attempt. One run may feed multiple typed result tables (the unified text run writes sentiment_results + favorability_stances).';
COMMENT ON COLUMN analysis.runs.is_current IS 'Latest-per-(subject,task) flag; the writer flips the predecessor to false in the same transaction that inserts the new current row. Replaces the correlated-MAX view pattern.';
COMMENT ON COLUMN analysis.runs.raw_response IS 'Verbatim LLM output — the audit payload. NULL for pure-deterministic runs.';
COMMENT ON COLUMN analysis.runs.prompt_version_id IS 'Nullable: deterministic runs (citation_extractor, lean_derivation) have no prompt.';

-- Latest-per-subject-per-task uniqueness, doc-scoped and author-scoped.
-- A partial unique index per scope (rather than one index over both nullable
-- columns) because a single index can't express "unique on whichever of the
-- two XOR columns is populated".
CREATE UNIQUE INDEX runs_current_doc_task_uq ON analysis.runs (doc_id, task)
    WHERE is_current AND doc_id IS NOT NULL;
CREATE UNIQUE INDEX runs_current_author_task_uq ON analysis.runs (author_id, task)
    WHERE is_current AND author_id IS NOT NULL;
CREATE INDEX idx_runs_doc ON analysis.runs (doc_id);
CREATE INDEX idx_runs_author ON analysis.runs (author_id);

CREATE TABLE analysis.sentiment_results (
    run_id BIGINT PRIMARY KEY REFERENCES analysis.runs (run_id),
    label analysis.sentiment_label NOT NULL,
    score REAL,
    sarcasm_detected BOOLEAN,
    evidence_spans TEXT[]
);

CREATE TABLE analysis.favorability_stances (
    favorability_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id BIGINT NOT NULL REFERENCES analysis.runs (run_id),
    entity_id BIGINT NOT NULL REFERENCES corpus.entities (entity_id),
    stance analysis.favorability_label NOT NULL,
    score REAL,
    evidence_spans TEXT[]
);
COMMENT ON TABLE analysis.favorability_stances IS 'One row per (run, entity) — a single text run can carry favorability toward more than one entity.';

CREATE INDEX idx_favorability_stances_run ON analysis.favorability_stances (run_id);
CREATE INDEX idx_favorability_stances_entity ON analysis.favorability_stances (entity_id);

CREATE TABLE analysis.target_mentions (
    mention_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id BIGINT NOT NULL REFERENCES analysis.runs (run_id),
    doc_id BIGINT NOT NULL REFERENCES corpus.documents (doc_id),
    raw_target TEXT NOT NULL,
    entity_id BIGINT REFERENCES corpus.entities (entity_id),
    stance analysis.sentiment_label NOT NULL,
    topic TEXT,
    confidence REAL NOT NULL,
    evidence_spans TEXT[],
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON COLUMN analysis.target_mentions.raw_target IS 'LLM''s original target string, kept verbatim for audit regardless of resolution.';
COMMENT ON COLUMN analysis.target_mentions.entity_id IS 'NULL = unresolved against the entity registry. Unresolved mentions persist (not dropped) — a registry edit never silently remaps history because resolution was frozen at extraction time.';

CREATE INDEX idx_target_mentions_doc ON analysis.target_mentions (doc_id);
CREATE INDEX idx_target_mentions_entity ON analysis.target_mentions (entity_id);
CREATE INDEX idx_target_mentions_run ON analysis.target_mentions (run_id);

CREATE TABLE analysis.propaganda_results (
    run_id BIGINT PRIMARY KEY REFERENCES analysis.runs (run_id),
    density REAL,
    summary TEXT
);
COMMENT ON COLUMN analysis.propaganda_results.density IS 'Overall propaganda-technique density score for the doc; the narrative rollup''s propaganda_score is a mean over these.';

CREATE TABLE analysis.propaganda_techniques (
    technique_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id BIGINT NOT NULL REFERENCES analysis.propaganda_results (run_id),
    technique analysis.propaganda_technique NOT NULL,
    evidence_span TEXT NOT NULL,
    confidence REAL
);
COMMENT ON COLUMN analysis.propaganda_techniques.evidence_span IS 'Verbatim quoted span the model flagged, not a paraphrase.';

CREATE INDEX idx_propaganda_techniques_run ON analysis.propaganda_techniques (run_id);

CREATE TABLE analysis.claims (
    claim_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id BIGINT NOT NULL REFERENCES analysis.runs (run_id),
    doc_id BIGINT NOT NULL REFERENCES corpus.documents (doc_id),
    claim_text TEXT NOT NULL,
    topic TEXT,
    confidence REAL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_claims_doc ON analysis.claims (doc_id);
CREATE INDEX idx_claims_run ON analysis.claims (run_id);

CREATE TABLE analysis.bot_signals (
    run_id BIGINT PRIMARY KEY REFERENCES analysis.runs (run_id),
    doc_id BIGINT NOT NULL REFERENCES corpus.documents (doc_id),
    label analysis.bot_label NOT NULL,
    score REAL,
    llm_text_likelihood REAL,
    burstiness REAL,
    type_token_ratio REAL,
    template_score REAL
);
COMMENT ON TABLE analysis.bot_signals IS 'Typed stylometrics used by rollups/thresholds; full detail (all raw features) stays in analysis.runs.raw_response.';

CREATE INDEX idx_bot_signals_doc ON analysis.bot_signals (doc_id);

CREATE TABLE analysis.author_bot_scores (
    author_id BIGINT PRIMARY KEY REFERENCES corpus.authors (author_id),
    score REAL NOT NULL,
    variance REAL,
    sample_count INTEGER NOT NULL,
    bot_post_count INTEGER NOT NULL DEFAULT 0,
    suspicious_post_count INTEGER NOT NULL DEFAULT 0,
    llm_text_likelihood_mean REAL,
    updated_at TIMESTAMPTZ NOT NULL
);
COMMENT ON TABLE analysis.author_bot_scores IS 'Materialized per-author rollup over bot_signals — the bot rollup becomes a plain SQL aggregate (Phase 6), this table is its output.';

CREATE TABLE analysis.author_leans (
    author_id BIGINT PRIMARY KEY REFERENCES corpus.authors (author_id),
    lean corpus.political_lean NOT NULL,
    lean_share REAL NOT NULL,
    lean_confidence REAL NOT NULL,
    stance_sample_count INTEGER NOT NULL,
    computed_at TIMESTAMPTZ NOT NULL
);
COMMENT ON TABLE analysis.author_leans IS 'Deterministic derived lean (engine/lean_derivation.py), computed once per pipeline run from target_mentions + favorability_stances stance evidence. Every consumer JOINs this — never re-derives. Below the module''s sample-count/threshold constants, lean = ''unknown'', never a guess.';
COMMENT ON COLUMN analysis.author_leans.lean_share IS 'Share of stance-evidence mass supporting `lean`, in [0,1].';

CREATE TABLE analysis.clustering_runs (
    clustering_run_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    mode TEXT NOT NULL,
    threshold REAL NOT NULL,
    embedding_model TEXT,
    started_at TIMESTAMPTZ NOT NULL,
    completed_at TIMESTAMPTZ,
    doc_count INTEGER
);
COMMENT ON TABLE analysis.clustering_runs IS 'Versioned provenance row per narrative_clusterer invocation. mode is jaccard|embedding (CIVIC_NARRATIVE_SIMILARITY_MODE).';

CREATE TABLE analysis.narratives (
    narrative_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    clustering_run_id BIGINT NOT NULL REFERENCES analysis.clustering_runs (clustering_run_id),
    name TEXT NOT NULL,
    description TEXT,
    anchor_claim_id BIGINT REFERENCES analysis.claims (claim_id),
    anchor_embedding REAL[],
    first_seen_at TIMESTAMPTZ,
    first_seen_doc_id BIGINT REFERENCES corpus.documents (doc_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ
);
COMMENT ON COLUMN analysis.narratives.first_seen_doc_id IS 'Earliest doc WE ingested carrying this claim — first-ingested-by-us, not claim origin in the world (see CLAUDE.md scope note / walkthrough 035).';
COMMENT ON COLUMN analysis.narratives.anchor_embedding IS 'REAL[] embedding vector; pgvector deferred per the plan (revisit if clustering-run scale demands it).';

CREATE INDEX idx_narratives_first_seen ON analysis.narratives (first_seen_at);
CREATE INDEX idx_narratives_clustering_run ON analysis.narratives (clustering_run_id);

CREATE TABLE analysis.narrative_leans (
    narrative_id BIGINT PRIMARY KEY REFERENCES analysis.narratives (narrative_id),
    lean corpus.political_lean NOT NULL,
    lean_share REAL NOT NULL,
    confidence REAL NOT NULL,
    doc_count INTEGER NOT NULL,
    computed_at TIMESTAMPTZ NOT NULL
);
COMMENT ON TABLE analysis.narrative_leans IS 'Same derivation module as author_leans, keyed by narrative instead of author.';

CREATE TABLE analysis.narrative_docs (
    narrative_id BIGINT NOT NULL REFERENCES analysis.narratives (narrative_id),
    doc_id BIGINT NOT NULL REFERENCES corpus.documents (doc_id),
    discovered_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    confidence REAL,
    PRIMARY KEY (narrative_id, doc_id)
);

CREATE INDEX idx_narrative_docs_doc ON analysis.narrative_docs (doc_id);
CREATE INDEX idx_narrative_docs_discovered ON analysis.narrative_docs (discovered_at);

CREATE TABLE analysis.citations (
    citation_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id BIGINT REFERENCES analysis.runs (run_id),
    source_doc_id BIGINT NOT NULL REFERENCES corpus.documents (doc_id),
    target_doc_id BIGINT REFERENCES corpus.documents (doc_id),
    target_url TEXT,
    link_type analysis.link_type NOT NULL,
    discovered_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT citations_target_xor CHECK ((target_doc_id IS NULL) <> (target_url IS NULL))
);
COMMENT ON TABLE analysis.citations IS 'Deterministic citation edges BETWEEN INGESTED DOCS (or from a doc to an un-ingested external URL) — not a claim about origin or full cross-medium propagation. run_id links back to the citation_extractor run that discovered it (deterministic runs now get a run row, unlike the old fake-marker convention).';

CREATE INDEX idx_citations_source ON analysis.citations (source_doc_id);
CREATE INDEX idx_citations_target ON analysis.citations (target_doc_id);
CREATE INDEX idx_citations_target_url ON analysis.citations (target_url);

CREATE TABLE analysis.evals (
    eval_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id BIGINT NOT NULL UNIQUE REFERENCES analysis.runs (run_id),
    verdict analysis.verdict NOT NULL,
    reviewer_id TEXT,
    notes TEXT,
    reviewed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE analysis.evals IS 'Verdict on ONE specific run — append-only-safe (a reprocess never invalidates a past eval, since it points at a specific run_id, not a (doc,task) pair).';

CREATE TABLE analysis.golden_labels (
    doc_id BIGINT NOT NULL REFERENCES corpus.documents (doc_id),
    task analysis.task NOT NULL,
    expected_label TEXT NOT NULL,
    source_eval_id BIGINT REFERENCES analysis.evals (eval_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (doc_id, task)
);
COMMENT ON TABLE analysis.golden_labels IS 'Run-independent expected answer keyed (doc,task), minted from an eval — future reprocess runs auto-score against it (recompute-proof), unlike evals which point at a specific run.';

-- =============================================================================
-- serving — the rollup seam (Python serving/ builders write; API reads only)
-- =============================================================================

CREATE TYPE serving.window AS ENUM ('24h', '7d', '30d', '90d');

CREATE TABLE serving.refreshes (
    rollup_name TEXT NOT NULL,
    win serving.window NOT NULL,
    computed_at TIMESTAMPTZ NOT NULL,
    row_count INTEGER NOT NULL,
    source_max_run_id BIGINT NOT NULL,
    PRIMARY KEY (rollup_name, win)
);
COMMENT ON TABLE serving.refreshes IS 'The freshness contract, replacing GET /snapshot-status. source_max_run_id is the watermark: the highest analysis.runs.run_id folded into this rollup as of computed_at.';

-- JUDGMENT CALL: the plan sketches "one table per panel family" but leaves
-- column sets loose beyond the four it names explicitly (window enum,
-- sentiment label/score/evidence_spans shape, sample_docs.source_url NOT
-- NULL, and lean columns on bot/narrative/entity rollups). The tables below
-- take a dimensional-rollup shape (one row per window x dimension x key)
-- rather than one column per legacy dashboard field, so a new UI cut is an
-- INSERT with a new dimension_key, not a new column/migration. This is
-- deliberately more normalized than the old aggregator_models.py dataclasses
-- (785 lines of nested payload) — the richest nested drill-down lists
-- (byTopic arrays, per-day series, per-sample evidence) are Phase 9 API
-- assembly logic reading FROM these narrow tables via GROUP BY / json_agg,
-- not stored as pre-nested JSONB (which the plan restricts to three columns
-- total, none in serving).

CREATE TABLE serving.sentiment_rollups (
    rollup_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    win serving.window NOT NULL,
    dimension TEXT NOT NULL,
    dimension_key TEXT NOT NULL,
    positive INTEGER NOT NULL DEFAULT 0,
    negative INTEGER NOT NULL DEFAULT 0,
    neutral INTEGER NOT NULL DEFAULT 0,
    mixed INTEGER NOT NULL DEFAULT 0,
    volume INTEGER NOT NULL DEFAULT 0,
    net_score REAL,
    sarcasm_rate REAL,
    computed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (win, dimension, dimension_key)
);
COMMENT ON TABLE serving.sentiment_rollups IS 'One row per (window, dimension, dimension_key): dimension in (''overall'',''platform'',''topic'',''time_window'',''day_of_week''); dimension_key is e.g. the platform name, topic name, or weekday label (empty string for ''overall''). Covers SentimentOverview/PlatformSentiment/TopicSentiment/TimeWindowSentiment/DayOfWeekSentiment from the old aggregator_models.py.';

CREATE TABLE serving.entity_stance_rollups (
    rollup_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    win serving.window NOT NULL,
    tier TEXT NOT NULL,
    entity_id BIGINT REFERENCES corpus.entities (entity_id),
    catch_all_key TEXT,
    positive INTEGER NOT NULL DEFAULT 0,
    negative INTEGER NOT NULL DEFAULT 0,
    neutral INTEGER NOT NULL DEFAULT 0,
    volume INTEGER NOT NULL DEFAULT 0,
    net_score REAL,
    engagement_total INTEGER NOT NULL DEFAULT 0,
    lean corpus.political_lean,
    lean_confidence REAL,
    computed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE serving.entity_stance_rollups IS 'EntitySentimentItem rollup (byNewsOutlet/byOfficial/byGeneralPublic). tier in (''news'',''officials'',''public''); entity_id NULL + catch_all_key set for pseudo-entities like other-outlets/general-public. lean is denormalized here (joined from entities.lean or author_leans at build time) — a lean-aware view is a JOIN at build time, not a new derivation, per the plan.';
COMMENT ON COLUMN serving.entity_stance_rollups.entity_id IS 'NULL for catch-all buckets; see catch_all_key.';

CREATE UNIQUE INDEX entity_stance_rollups_uq ON serving.entity_stance_rollups (
    win, tier, COALESCE(entity_id, -1), COALESCE(catch_all_key, '')
);
CREATE INDEX idx_entity_stance_rollups_entity ON serving.entity_stance_rollups (entity_id);

CREATE TABLE serving.sample_docs (
    sample_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    win serving.window NOT NULL,
    rollup_name TEXT NOT NULL,
    bucket_key TEXT,
    doc_id BIGINT NOT NULL REFERENCES corpus.documents (doc_id),
    run_id BIGINT REFERENCES analysis.runs (run_id),
    label TEXT,
    confidence REAL,
    reasoning TEXT,
    evidence_spans TEXT[],
    source_url TEXT NOT NULL,
    rank INTEGER,
    computed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE serving.sample_docs IS 'Evidence samples backing every drill-down modal (ClassificationSample / FlaggedExample in the old models). rollup_name + bucket_key identify which panel/bucket the sample supports, e.g. (''sentiment_distribution'',''strongPositive'') or (''bot_flagged'', entity_key).';
COMMENT ON COLUMN serving.sample_docs.source_url IS 'NOT NULL — invariant C1: every surfaced evidence sample carries a source link back to the original.';

CREATE INDEX idx_sample_docs_bucket ON serving.sample_docs (win, rollup_name, bucket_key);
CREATE INDEX idx_sample_docs_doc ON serving.sample_docs (doc_id);

CREATE TABLE serving.narrative_rollups (
    narrative_id BIGINT NOT NULL REFERENCES analysis.narratives (narrative_id),
    win serving.window NOT NULL,
    supporting_doc_count INTEGER NOT NULL DEFAULT 0,
    net_sentiment REAL,
    inbound_citation_count INTEGER NOT NULL DEFAULT 0,
    external_citation_count INTEGER NOT NULL DEFAULT 0,
    propaganda_score REAL,
    bot_pushed_fraction REAL,
    mean_confidence REAL,
    first_seen_doc_id BIGINT REFERENCES corpus.documents (doc_id),
    lean corpus.political_lean,
    lean_share REAL,
    computed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (narrative_id, win)
);
COMMENT ON TABLE serving.narrative_rollups IS 'NarrativeSummary rollup. lean/lean_share denormalized from analysis.narrative_leans at build time (gains the lean dimension per the plan). Nested lists (source_breakdown, timeline, top_supporting_docs, cross_narrative_citations, byTopic-style breakdowns) are assembled by the Phase 9 API layer from analysis.narrative_docs/citations/target_mentions directly, not stored here.';

CREATE TABLE serving.propaganda_rollups (
    rollup_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    win serving.window NOT NULL,
    technique analysis.propaganda_technique NOT NULL,
    doc_count INTEGER NOT NULL DEFAULT 0,
    mean_confidence REAL,
    computed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (win, technique)
);

CREATE TABLE serving.bot_rollups (
    rollup_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    win serving.window NOT NULL,
    tier TEXT NOT NULL,
    entity_id BIGINT REFERENCES corpus.entities (entity_id),
    catch_all_key TEXT,
    total_docs INTEGER NOT NULL DEFAULT 0,
    bot_docs INTEGER NOT NULL DEFAULT 0,
    bot_rate_pct REAL,
    lean corpus.political_lean,
    computed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE serving.bot_rollups IS 'BotEntityItem rollup. lean denormalized from author_leans/entities at build time — gains the lean dimension per the plan (a bot-flagged account labeled "bot, leaning democrat" when stance mass is one-sided).';

CREATE UNIQUE INDEX bot_rollups_uq ON serving.bot_rollups (
    win, tier, COALESCE(entity_id, -1), COALESCE(catch_all_key, '')
);

CREATE TABLE serving.outlet_profiles (
    rollup_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    win serving.window NOT NULL,
    entity_id BIGINT REFERENCES corpus.entities (entity_id),
    outlet_key TEXT NOT NULL,
    source_type corpus.source_type NOT NULL,
    net_tone REAL,
    bot_rate_pct REAL NOT NULL DEFAULT 0,
    volume INTEGER NOT NULL DEFAULT 0,
    total_scanned INTEGER NOT NULL DEFAULT 0,
    computed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (win, outlet_key, source_type)
);
COMMENT ON TABLE serving.outlet_profiles IS 'OutletProfile rollup: outlet_key is the raw domain/subreddit as stored on documents, independent of registry match (unregistered outlets still get a row, catch-all rollups live in entity_stance_rollups/bot_rollups separately).';

CREATE TABLE serving.movers (
    mover_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    win serving.window NOT NULL,
    kind TEXT NOT NULL,
    entity_id BIGINT REFERENCES corpus.entities (entity_id),
    display_name TEXT NOT NULL,
    current_net REAL,
    prev_net REAL,
    delta_pts REAL,
    current_volume INTEGER NOT NULL DEFAULT 0,
    prev_volume INTEGER NOT NULL DEFAULT 0,
    computed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE serving.movers IS 'Window-over-window tone/favorability movers for the ticker. kind in (''outlet'',''official'',''subreddit'',''favorability''); entity_id NULL for the single favorability_mover row (kind=''favorability'', no registry entity).';

CREATE INDEX idx_movers_window ON serving.movers (win);

-- =============================================================================
-- ops — pipeline machinery
-- =============================================================================

CREATE TYPE ops.task_status AS ENUM ('pending', 'in_progress', 'done', 'failed');

CREATE TABLE ops.task_queue (
    doc_id BIGINT NOT NULL REFERENCES corpus.documents (doc_id),
    task analysis.task NOT NULL,
    status ops.task_status NOT NULL DEFAULT 'pending',
    attempts INTEGER NOT NULL DEFAULT 0,
    last_error TEXT,
    claimed_at TIMESTAMPTZ,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (doc_id, task)
);
COMMENT ON TABLE ops.task_queue IS 'ETL seeds pending rows; workers claim with FOR UPDATE SKIP LOCKED and write results directly (the serialized SQLite write-back thread dies). Reprocess = UPDATE to pending, never DELETE. Stale in_progress rows are reset to pending at pipeline start (frontier crash-safety pattern).';

CREATE INDEX idx_task_queue_task_status ON ops.task_queue (task, status);

CREATE TABLE ops.pipeline_runs (
    pipeline_run_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    started_at TIMESTAMPTZ NOT NULL,
    completed_at TIMESTAMPTZ,
    status TEXT NOT NULL DEFAULT 'running',
    stage_summary JSONB
);
COMMENT ON COLUMN ops.pipeline_runs.stage_summary IS 'Per-stage counts/timings/errors for one job_runner.py invocation — the third and last JSONB column in the schema.';

CREATE TABLE ops.x_api_budget (
    month_key TEXT PRIMARY KEY,
    post_count INTEGER NOT NULL DEFAULT 0,
    user_count INTEGER NOT NULL DEFAULT 0,
    request_count INTEGER NOT NULL DEFAULT 0,
    estimated_cents INTEGER NOT NULL DEFAULT 0,
    last_updated TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON COLUMN ops.x_api_budget.month_key IS 'YYYY-MM, UTC calendar month.';

-- IF NOT EXISTS: same bootstrap-collision reason as the schema above. Column
-- definitions here must stay byte-identical to the bootstrap's in db.go
-- (version INT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT now())
-- since whichever one runs first wins and the other becomes a no-op.
CREATE TABLE IF NOT EXISTS ops.schema_migrations (
    version INTEGER PRIMARY KEY,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE ops.schema_migrations IS 'Applied-migration ledger for the ported Go migration runner (ingest/internal/storage/db/db.go). This file (0001) is the first row.';

-- =============================================================================
-- archive — verbatim one-time import of the old SQLite derived data
-- =============================================================================
--
-- Read-only by convention: no FKs beyond primary keys, no CHECK constraints,
-- no new code anywhere in the codebase reads or writes these tables except
-- tools/migrate_sqlite_to_pg.py --archive (Phase 3, one-time). Primary keys
-- preserve the imported SQLite integer IDs verbatim (NOT GENERATED ALWAYS AS
-- IDENTITY) — traceability to the cold R2 artifact depends on the old row
-- numbers surviving unchanged. Epoch integer columns become TIMESTAMPTZ via
-- to_timestamp(); TEXT JSON columns become JSONB for browsability; the
-- original bytes remain in the age-encrypted SQLite file in R2 regardless.

CREATE TABLE archive.docs (
    doc_id BIGINT PRIMARY KEY,
    source_type TEXT,
    ident TEXT,
    domain_or_subreddit TEXT,
    published_at TIMESTAMPTZ,
    title TEXT,
    text TEXT,
    raw_hash TEXT,
    metadata_json JSONB,
    etl_version TEXT
);

CREATE TABLE archive.prompt_versions (
    prompt_version TEXT PRIMARY KEY,
    task_type TEXT,
    system_prompt TEXT,
    user_prompt_template TEXT,
    created_at TIMESTAMPTZ,
    notes TEXT
);

CREATE TABLE archive.ai_outputs (
    output_id BIGINT PRIMARY KEY,
    doc_id BIGINT,
    model_id TEXT,
    prompt_version TEXT,
    task_type TEXT,
    output_json JSONB,
    confidence REAL,
    created_at TIMESTAMPTZ,
    inference_method TEXT,
    label TEXT
);

CREATE TABLE archive.target_mentions (
    mention_id BIGINT PRIMARY KEY,
    output_id BIGINT,
    doc_id BIGINT,
    raw_target TEXT,
    entity_key TEXT,
    entity_kind TEXT,
    entity_party TEXT,
    stance TEXT,
    topic TEXT,
    confidence REAL,
    evidence_json JSONB,
    created_at TIMESTAMPTZ
);

CREATE TABLE archive.narratives (
    narrative_id BIGINT PRIMARY KEY,
    name TEXT,
    description TEXT,
    first_seen_at TIMESTAMPTZ,
    first_seen_doc_id BIGINT,
    created_at TIMESTAMPTZ,
    updated_at TIMESTAMPTZ,
    anchor_embedding_json JSONB,
    clustering_mode TEXT,
    clustering_threshold REAL,
    embedding_model TEXT
);

CREATE TABLE archive.narrative_docs (
    assignment_id BIGINT PRIMARY KEY,
    narrative_id BIGINT,
    doc_id BIGINT,
    discovered_at TIMESTAMPTZ,
    confidence REAL
);

CREATE TABLE archive.narrative_citations (
    citation_id BIGINT PRIMARY KEY,
    source_doc_id BIGINT,
    target_doc_id BIGINT,
    target_url TEXT,
    link_type TEXT,
    discovered_at TIMESTAMPTZ
);

CREATE TABLE archive.account_profiles (
    profile_id BIGINT PRIMARY KEY,
    platform TEXT,
    author_id TEXT,
    display_name TEXT,
    tier TEXT,
    classification_method TEXT,
    classified_at TIMESTAMPTZ,
    notes TEXT,
    full_name TEXT,
    party TEXT,
    branch TEXT,
    chamber TEXT,
    state_or_district TEXT,
    office_title TEXT,
    account_type TEXT
);

CREATE TABLE archive.author_bot_scores (
    platform TEXT,
    author_id TEXT,
    score REAL,
    variance REAL,
    sample_count INTEGER,
    bot_post_count INTEGER,
    suspicious_post_count INTEGER,
    llm_text_likelihood_mean REAL,
    stylometric_features_json JSONB,
    updated_at TIMESTAMPTZ,
    PRIMARY KEY (platform, author_id)
);

CREATE TABLE archive.doc_task_state (
    doc_id BIGINT,
    task_type TEXT,
    status TEXT,
    prompt_version TEXT,
    attempts INTEGER,
    last_attempt_at TIMESTAMPTZ,
    PRIMARY KEY (doc_id, task_type)
);

CREATE TABLE archive.ai_output_evals (
    eval_id BIGINT PRIMARY KEY,
    ai_output_id BIGINT,
    doc_id BIGINT,
    task_type TEXT,
    human_label TEXT,
    human_confidence REAL,
    is_correct BOOLEAN,
    is_golden BOOLEAN,
    reviewer_id TEXT,
    reviewed_at TIMESTAMPTZ,
    notes TEXT
);
COMMENT ON TABLE archive.ai_output_evals IS 'Expected empty at import time (production ai_output_evals confirmed 0 rows as of the 2026-07-22 plan pull) — imported anyway for schema completeness in case that changes before Phase 3 runs.';

-- =============================================================================
-- Role setup (template — role names are decided at deploy, not here)
-- =============================================================================
-- The application role must never mutate archive.* (read-only by
-- convention). Run manually at deploy time once the role name is chosen;
-- not executed by this migration.
--
-- REVOKE INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA archive FROM civic_app;
-- ALTER DEFAULT PRIVILEGES IN SCHEMA archive REVOKE INSERT, UPDATE, DELETE ON TABLES FROM civic_app;
-- GRANT SELECT ON ALL TABLES IN SCHEMA archive TO civic_app;
-- GRANT USAGE ON SCHEMA archive TO civic_app;

-- NOTE: this file does not self-insert its own ops.schema_migrations row.
-- The Go runner's applyPostgresMigration records (version, applied_at) for
-- every migration file in the same transaction as the file's own SQL (see
-- db.go) — migration files staying silent on their own version is the
-- established contract (mirrored by the fixture migrations in
-- db_postgres_test.go, none of which self-insert either). An earlier draft
-- of this file inserted (1, now()) here too, which duplicated that recording
-- and made the runner's own insert fail on the primary key; removed.
