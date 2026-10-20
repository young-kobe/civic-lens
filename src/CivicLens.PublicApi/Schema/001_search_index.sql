CREATE TABLE releases (
    release_number integer PRIMARY KEY CHECK (release_number >= 1),
    published_at_utc timestamptz NOT NULL
);

CREATE TABLE active_release (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    release_number integer NOT NULL REFERENCES releases (release_number)
);

CREATE TABLE records (
    record_id text NOT NULL,
    revision_number integer NOT NULL CHECK (revision_number >= 1),
    kind text NOT NULL,
    headline text NOT NULL,
    first_published_at_utc timestamptz NOT NULL,
    officials jsonb NOT NULL,
    official_ids text[] NOT NULL,
    issue_ids text[] NOT NULL,
    policy_area_ids text[] NOT NULL,
    source_host text,
    event_date date,
    body text NOT NULL,
    search tsvector NOT NULL,
    first_release integer NOT NULL REFERENCES releases (release_number),
    last_release integer REFERENCES releases (release_number),
    PRIMARY KEY (record_id, revision_number, first_release),
    CHECK (last_release IS NULL OR last_release >= first_release)
);

CREATE INDEX ix_records_search ON records USING gin (search);
CREATE INDEX ix_records_official_ids ON records USING gin (official_ids);
CREATE INDEX ix_records_issue_ids ON records USING gin (issue_ids);
CREATE INDEX ix_records_policy_area_ids ON records USING gin (policy_area_ids);
CREATE INDEX ix_records_open ON records (record_id) WHERE last_release IS NULL;
