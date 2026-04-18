-- 007_audit_and_propagation_tables.sql
-- Adds four new areas of the schema required for auditability and narrative-
-- propagation analysis (see docs/audits/04_16_2026.md).
--
-- Tables in this migration:
--   prompt_versions       — one row per prompt version with full system-prompt text.
--                           Writers: save_ai_output() upserts on every inference.
--   ai_output_evals       — human override / golden-set marker for an ai_outputs row.
--                           Writers: none yet — a review UI or CLI will populate these.
--   narratives            — identity for a distinct claim/talking point.
--                           Writers: none yet — narrative-extraction pipeline TBD.
--   narrative_docs        — membership edge: doc belongs to narrative.
--                           Writers: none yet.
--   narrative_citations   — causal edge: source_doc cites / quotes / replies target.
--                           Writers: ingestion or analysis-time linker TBD.

-- =========================================================================
-- Prompt audit
-- =========================================================================

CREATE TABLE IF NOT EXISTS prompt_versions (
    prompt_version TEXT PRIMARY KEY,
    task_type TEXT NOT NULL,
    system_prompt TEXT NOT NULL,
    user_prompt_template TEXT,
    created_at INTEGER NOT NULL,
    notes TEXT
);

CREATE INDEX IF NOT EXISTS idx_prompt_versions_task ON prompt_versions(task_type);

-- =========================================================================
-- Human review / golden-set marker
-- =========================================================================

CREATE TABLE IF NOT EXISTS ai_output_evals (
    eval_id INTEGER PRIMARY KEY AUTOINCREMENT,
    ai_output_id INTEGER NOT NULL UNIQUE,
    doc_id INTEGER NOT NULL,
    task_type TEXT NOT NULL,
    human_label TEXT,
    human_confidence REAL,
    is_correct INTEGER,           -- 1 correct, 0 incorrect, NULL unreviewed
    is_golden INTEGER NOT NULL DEFAULT 0,
    reviewer_id TEXT,
    reviewed_at INTEGER NOT NULL,
    notes TEXT,
    FOREIGN KEY(ai_output_id) REFERENCES ai_outputs(output_id),
    FOREIGN KEY(doc_id) REFERENCES docs(doc_id)
);

CREATE INDEX IF NOT EXISTS idx_ai_output_evals_doc ON ai_output_evals(doc_id);
CREATE INDEX IF NOT EXISTS idx_ai_output_evals_golden ON ai_output_evals(is_golden, task_type);

-- =========================================================================
-- Narrative identity and propagation
-- =========================================================================

CREATE TABLE IF NOT EXISTS narratives (
    narrative_id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    description TEXT,
    first_seen_at INTEGER,
    origin_doc_id INTEGER,
    created_at INTEGER NOT NULL,
    updated_at INTEGER,
    FOREIGN KEY(origin_doc_id) REFERENCES docs(doc_id)
);

CREATE INDEX IF NOT EXISTS idx_narratives_first_seen ON narratives(first_seen_at);

CREATE TABLE IF NOT EXISTS narrative_docs (
    assignment_id INTEGER PRIMARY KEY AUTOINCREMENT,
    narrative_id INTEGER NOT NULL,
    doc_id INTEGER NOT NULL,
    discovered_at INTEGER NOT NULL,
    confidence REAL,
    UNIQUE(narrative_id, doc_id),
    FOREIGN KEY(narrative_id) REFERENCES narratives(narrative_id),
    FOREIGN KEY(doc_id) REFERENCES docs(doc_id)
);

CREATE INDEX IF NOT EXISTS idx_narrative_docs_narrative ON narrative_docs(narrative_id);
CREATE INDEX IF NOT EXISTS idx_narrative_docs_doc ON narrative_docs(doc_id);
CREATE INDEX IF NOT EXISTS idx_narrative_docs_discovered ON narrative_docs(discovered_at);

-- Cross-source citation edges. source_doc_id is always a doc we own; target is
-- either one of our docs (target_doc_id) or an external URL we have not ingested
-- (target_url). Exactly one should be set.
CREATE TABLE IF NOT EXISTS narrative_citations (
    citation_id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_doc_id INTEGER NOT NULL,
    target_doc_id INTEGER,
    target_url TEXT,
    link_type TEXT NOT NULL CHECK(link_type IN ('url_citation', 'quote', 'reply', 'retweet', 'repost')),
    discovered_at INTEGER NOT NULL,
    CHECK ((target_doc_id IS NOT NULL) OR (target_url IS NOT NULL)),
    FOREIGN KEY(source_doc_id) REFERENCES docs(doc_id),
    FOREIGN KEY(target_doc_id) REFERENCES docs(doc_id)
);

CREATE INDEX IF NOT EXISTS idx_narrative_citations_source ON narrative_citations(source_doc_id);
CREATE INDEX IF NOT EXISTS idx_narrative_citations_target ON narrative_citations(target_doc_id);
CREATE INDEX IF NOT EXISTS idx_narrative_citations_target_url ON narrative_citations(target_url);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (7, strftime('%s', 'now'));
