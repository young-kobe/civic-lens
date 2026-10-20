CREATE TABLE reader_reports (
    sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    report_id uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
    record_id text NOT NULL,
    revision_number integer NOT NULL CHECK (revision_number >= 1),
    kind text NOT NULL,
    release_number integer NOT NULL CHECK (release_number >= 1),
    category text NOT NULL,
    body text NOT NULL,
    payload_hash bytea NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (record_id, revision_number, payload_hash)
);

CREATE INDEX ix_reader_reports_received_at ON reader_reports (received_at);

CREATE TABLE report_pull_state (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    epoch uuid NOT NULL,
    acked_sequence bigint NOT NULL DEFAULT 0 CHECK (acked_sequence >= 0)
);

INSERT INTO report_pull_state (singleton, epoch) VALUES (true, gen_random_uuid());
