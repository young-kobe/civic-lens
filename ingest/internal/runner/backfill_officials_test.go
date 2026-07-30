package runner

import (
	"testing"
)

// TestPostsNeeded_ShortfallMath exercises the pure shortfall calculation
// backfill-officials relies on for its resumability contract: a stored
// count at or above target never yields a negative "need more" value, it
// yields exactly zero (nothing left to fetch).
func TestPostsNeeded_ShortfallMath(t *testing.T) {
	cases := []struct {
		name           string
		target, stored int
		want           int
	}{
		{"fresh official, editorial target", 100, 0, 100},
		{"partially backfilled, promoted target", 25, 10, 15},
		{"exactly at target", 25, 25, 0},
		{"already past target", 25, 40, 0},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			if got := postsNeeded(c.target, c.stored); got != c.want {
				t.Errorf("postsNeeded(%d, %d) = %d, want %d", c.target, c.stored, got, c.want)
			}
		})
	}
}

// TestTargetFor_EditorialVsPromoted pins the two named constants to the
// provenance column that selects between them — a regression here would
// silently under- or over-fetch an entire provenance tier.
func TestTargetFor_EditorialVsPromoted(t *testing.T) {
	if got := targetFor(officialRow{Handle: "leaderjohnthune", Editorial: true}); got != editorialBackfillTarget {
		t.Errorf("editorial target = %d, want %d", got, editorialBackfillTarget)
	}
	if got := targetFor(officialRow{Handle: "austinscottga08", Editorial: false}); got != promotedBackfillTarget {
		t.Errorf("promoted target = %d, want %d", got, promotedBackfillTarget)
	}
}
