package runner

import "time"

// unixOrNil converts a Unix-epoch-seconds value into a *time.Time for a
// nullable Postgres TIMESTAMPTZ column, returning nil when sec is the
// zero-value sentinel the SQLite-era model structs use for "not set".
// Storing epoch-zero (1970-01-01) in a real TIMESTAMPTZ column would read as
// a fabricated timestamp rather than "unknown" (see the media-analysis
// invariant against fabricating data) — nil lets Postgres store SQL NULL
// instead.
func unixOrNil(sec int64) *time.Time {
	if sec == 0 {
		return nil
	}
	t := time.Unix(sec, 0).UTC()
	return &t
}

// unixTime converts a Unix-epoch-seconds value into a time.Time for a NOT
// NULL Postgres TIMESTAMPTZ column that the caller has already guaranteed is
// populated (e.g. fetched_at, always set to time.Now().Unix() at write time).
func unixTime(sec int64) time.Time {
	return time.Unix(sec, 0).UTC()
}

// nullableJSON returns s as a bind parameter for a JSONB column, or nil
// (SQL NULL) when s is empty. The model structs serialize optional JSON
// blobs (e.g. XPost.ContextAnnotationsJSON) to "" when there is nothing to
// record; an empty string is not valid JSON and would fail the ::jsonb cast.
func nullableJSON(s string) any {
	if s == "" {
		return nil
	}
	return s
}
