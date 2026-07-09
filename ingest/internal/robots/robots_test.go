package robots

import (
	"strings"
	"testing"
)

// check runs a robots.txt body against a path for a given crawler UA.
func check(ua, body, path string) bool {
	c := &Checker{userAgent: ua}
	data := &robotsData{groups: parseRobots(strings.NewReader(body))}
	return c.checkPath(data, path)
}

// TestCheckPath_LongestMatchWins covers the I-8 core defect: an `Allow: /`
// used to short-circuit before any Disallow was consulted, so every Disallow
// in the group was nullified. Longest-match must let `Disallow: /private/`
// win over `Allow: /` for /private/ paths while still allowing the rest.
func TestCheckPath_LongestMatchWins(t *testing.T) {
	body := `User-agent: *
Allow: /
Disallow: /private/
`
	if check("CivicLens", body, "/private/secret") {
		t.Error("/private/secret should be disallowed (longest match is Disallow: /private/)")
	}
	if !check("CivicLens", body, "/public/story") {
		t.Error("/public/story should be allowed (only Allow: / matches)")
	}
}

// TestCheckPath_AllowWinsTies asserts that when an Allow and a Disallow match
// with equal specificity, Allow wins (standard robots semantics).
func TestCheckPath_AllowWinsTies(t *testing.T) {
	body := `User-agent: *
Disallow: /path
Allow: /path
`
	if !check("CivicLens", body, "/path/x") {
		t.Error("equal-length Allow should win the tie against Disallow")
	}
}

// TestCheckPath_SharedGroupAcrossConsecutiveUserAgents covers the group-
// splitting bug: consecutive User-agent lines share one directive block.
func TestCheckPath_SharedGroupAcrossConsecutiveUserAgents(t *testing.T) {
	body := `User-agent: BadBot
User-agent: CivicLens
Disallow: /no-crawl/
`
	if check("CivicLens/1.0", body, "/no-crawl/page") {
		t.Error("CivicLens shares the group's Disallow with BadBot; /no-crawl/ must be blocked")
	}
	if !check("CivicLens/1.0", body, "/ok") {
		t.Error("/ok should be allowed")
	}
}

// TestCheckPath_CaseInsensitiveUA asserts UA matching ignores case.
func TestCheckPath_CaseInsensitiveUA(t *testing.T) {
	body := `User-agent: civiclens
Disallow: /blocked/
`
	if check("CivicLens/1.0 (+https://example.com)", body, "/blocked/x") {
		t.Error("UA match must be case-insensitive; /blocked/ should be disallowed")
	}
}

// TestCheckPath_MostSpecificBlockWins asserts the longest matching UA token's
// group governs, not the first-listed one — and the wildcard is only a
// fallback.
func TestCheckPath_MostSpecificBlockWins(t *testing.T) {
	body := `User-agent: *
Disallow: /

User-agent: CivicLens
Disallow: /admin/
`
	// The specific CivicLens block (only /admin/ blocked) must govern, not the
	// wildcard block that disallows everything.
	if !check("CivicLens/1.0", body, "/news/story") {
		t.Error("specific CivicLens group should allow /news/story despite wildcard Disallow: /")
	}
	if check("CivicLens/1.0", body, "/admin/panel") {
		t.Error("/admin/panel should be disallowed by the CivicLens group")
	}
	// A UA that matches only the wildcard is blocked everywhere.
	if check("SomeOtherBot", body, "/news/story") {
		t.Error("unlisted UA falls back to wildcard Disallow: /")
	}
}

// TestCheckPath_NoRobotsAllowsAll asserts an empty ruleset allows everything.
func TestCheckPath_NoRobotsAllowsAll(t *testing.T) {
	if !check("CivicLens", "", "/anything") {
		t.Error("empty robots.txt should allow all")
	}
}

// TestCheckPath_EmptyDisallowMeansAllow asserts `Disallow:` with no value is
// an explicit allow-all, not a block.
func TestCheckPath_EmptyDisallowMeansAllow(t *testing.T) {
	body := `User-agent: *
Disallow:
`
	if !check("CivicLens", body, "/anything") {
		t.Error("empty Disallow means allow-all")
	}
}
