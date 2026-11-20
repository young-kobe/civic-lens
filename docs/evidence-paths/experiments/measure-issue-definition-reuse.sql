-- Bounded PostgreSQL measurement of retained published rows at the research cutoff.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '2s';
SET LOCAL lock_timeout = '100ms';
WITH sampled AS (
    SELECT release_number FROM publication_releases
    WHERE release_number <= 1435 ORDER BY release_number DESC LIMIT 12
), issues AS (
    SELECT r.release_number, r.issue_id, r.name, r.definition, r.count_congress, r.bills, r.substantive_votes,
           ARRAY(SELECT i.text FROM release_issue_includes i WHERE (i.release_number, i.issue_id) = (r.release_number, r.issue_id) ORDER BY i.position) AS includes,
           ARRAY(SELECT e.text FROM release_issue_excludes e WHERE (e.release_number, e.issue_id) = (r.release_number, r.issue_id) ORDER BY e.position) AS excludes
    FROM sampled s JOIN release_issues r USING (release_number)
)
SELECT (SELECT count(*) FROM sampled) AS releases,
       (SELECT min(release_number) FROM sampled) AS oldest,
       (SELECT max(release_number) FROM sampled) AS newest,
       count(*) AS release_issue_rows, count(DISTINCT issue_id) AS issues,
       count(DISTINCT (issue_id, name, definition, includes, excludes)) AS public_definitions,
       count(DISTINCT (issue_id, count_congress, bills, substantive_votes)) AS distinct_count_rows
FROM issues;
ROLLBACK;
