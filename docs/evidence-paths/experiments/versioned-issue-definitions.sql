-- Synthetic release fixtures to check the proposed join. Run in empty SQLite.
PRAGMA foreign_keys = ON;
CREATE TABLE definition (
    issue TEXT NOT NULL,
    version INTEGER NOT NULL,
    name TEXT NOT NULL,
    wording TEXT,
    PRIMARY KEY (issue, version)
);

CREATE TABLE release_issue (
    release INTEGER NOT NULL,
    issue TEXT NOT NULL,
    version INTEGER NOT NULL,
    bills INTEGER,
    PRIMARY KEY (release, issue),
    FOREIGN KEY (issue, version) REFERENCES definition (issue, version)
);

-- Short example wording and counts. The legacy entry models a missing definition.
INSERT INTO definition VALUES
    ('middle-east-conflicts', 1, 'Middle East conflicts', 'Example wording A'),
    ('middle-east-conflicts', 2, 'Middle East conflicts', 'Example wording B'),
    ('artificial-intelligence', 1, 'Artificial intelligence', 'Example wording C'),
    ('legacy-id', 1, 'legacy-id', NULL);
INSERT INTO release_issue VALUES
    (1, 'middle-east-conflicts', 1, 3),
    (1, 'legacy-id', 1, NULL),
    (2, 'middle-east-conflicts', 1, 5),
    (3, 'middle-east-conflicts', 2, 5);

CREATE VIEW visible_issue AS
SELECT r.release, r.issue, d.version, d.name, d.wording, r.bills
FROM release_issue r
JOIN definition d ON d.issue = r.issue AND d.version = r.version;

-- Read release 1 again after release 3, as on rollback.
WITH read_order(step, release) AS (VALUES (1, 1), (2, 2), (3, 3), (4, 1))
SELECT s.step, v.release, v.issue, v.version, v.wording, v.bills
FROM read_order s JOIN visible_issue v ON v.release = s.release
ORDER BY s.step, v.issue;
