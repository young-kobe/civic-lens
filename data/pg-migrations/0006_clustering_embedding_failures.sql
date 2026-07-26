-- Narrative clustering is embedding-only as of 2026-07-26 (the lexical
-- jaccard comparator is gone). A failed embed() call leaves its claim
-- unclustered instead of quietly re-measuring it by word overlap, so the
-- count has to be visible: without it, clustering_runs.mode says
-- 'embedding' while some rows in the run were produced another way.
--
-- Existing rows get 0. That is accurate for historical 'jaccard' runs (no
-- embedding was attempted) and for 'embedding' runs it understates the
-- truth -- those pre-date the counter and their fallbacks were only ever
-- logged. Read clustering_runs.mode alongside this column.

ALTER TABLE analysis.clustering_runs
    ADD COLUMN embedding_failures INTEGER NOT NULL DEFAULT 0;

COMMENT ON COLUMN analysis.clustering_runs.embedding_failures IS
    'Embed calls that returned no vector during this run. Those claims went unclustered -- they were NOT clustered by another measure. Non-zero means the run is incomplete, not that no narratives exist.';
