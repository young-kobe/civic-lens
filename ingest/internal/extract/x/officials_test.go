package x

import (
	"context"
	"net/http"
	"net/http/httptest"
	"net/url"
	"os"
	"path/filepath"
	"testing"
)

const sampleOfficialsYAML = `
officials:
  - handle: "@POTUS"
    also_handles:
      - "@realDonaldTrump"
      - "@WhiteHouse"
    display_name: Donald J. Trump
    office: President of the United States
    party: R
    term_start: "2025-01-20"
    bio_source: "https://en.wikipedia.org/wiki/Donald_Trump"
  - handle: "@VP"
    also_handles:
      - "@JDVance"
    display_name: JD Vance
    office: Vice President of the United States
    party: R
    term_start: "2025-01-20"
    bio_source: "https://en.wikipedia.org/wiki/JD_Vance"
  - handle: ""
    display_name: garbage row
`

func writeYAML(t *testing.T, body string) string {
	t.Helper()
	dir := t.TempDir()
	path := filepath.Join(dir, "verified_officials.yaml")
	if err := os.WriteFile(path, []byte(body), 0o600); err != nil {
		t.Fatalf("write: %v", err)
	}
	return path
}

func TestLoadVerifiedOfficials_ParsesEntriesAndNormalizesHandles(t *testing.T) {
	path := writeYAML(t, sampleOfficialsYAML)
	got, err := LoadVerifiedOfficials(path)
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	// The empty-handle row is dropped.
	if len(got) != 2 {
		t.Fatalf("want 2 officials, got %d", len(got))
	}

	first := got[0]
	if first.Handle != "potus" {
		t.Errorf("primary handle should be lowercased without @: got %q", first.Handle)
	}
	wantAlts := []string{"realdonaldtrump", "whitehouse"}
	if len(first.AlsoHandles) != len(wantAlts) {
		t.Fatalf("alts: want %v got %v", wantAlts, first.AlsoHandles)
	}
	for i, h := range wantAlts {
		if first.AlsoHandles[i] != h {
			t.Errorf("alt %d: want %q got %q", i, h, first.AlsoHandles[i])
		}
	}
	if first.DisplayName != "Donald J. Trump" {
		t.Errorf("display name dropped: got %q", first.DisplayName)
	}
	if first.Party != "R" || first.TermStart != "2025-01-20" {
		t.Errorf("editorial fields lost: %+v", first)
	}

	all := first.AllHandles()
	if len(all) != 3 || all[0] != "potus" || all[2] != "whitehouse" {
		t.Errorf("AllHandles ordering wrong: %v", all)
	}
}

func TestLoadVerifiedOfficials_MissingFileReturnsEmpty(t *testing.T) {
	got, err := LoadVerifiedOfficials("/no/such/path.yaml")
	if err != nil {
		t.Fatalf("missing file should not error: %v", err)
	}
	if len(got) != 0 {
		t.Fatalf("want empty, got %d entries", len(got))
	}
}

func TestLoadVerifiedOfficials_MalformedYAMLErrors(t *testing.T) {
	path := writeYAML(t, "officials: [: not valid")
	if _, err := LoadVerifiedOfficials(path); err == nil {
		t.Fatal("want error from malformed YAML")
	}
}

// TestSearchRecentPosts_PadsMaxResultsAboveAPIMin pins down the defensive
// floor that prevented production from failing every topic-search query
// with HTTP 400 when `max_tweets_per_query: 8` slipped into seeds.yaml.
// The X /2/tweets/search/recent endpoint requires max_results in [10,100];
// the client must silently raise smaller values rather than propagate the
// 400.
func TestSearchRecentPosts_PadsMaxResultsAboveAPIMin(t *testing.T) {
	cases := []struct {
		name      string
		requested int
		wantParam string
	}{
		{"below floor pads to 10", 8, "10"},
		{"floor preserved", 10, "10"},
		{"in-range preserved", 50, "50"},
		{"above ceiling clamps to 100", 250, "100"},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			var seen url.Values
			srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				seen = r.URL.Query()
				_, _ = w.Write([]byte(`{"data":[]}`))
			}))
			defer srv.Close()

			client := New(Config{
				BearerToken:     "t",
				UserAgent:       "test",
				MaxRequestsHour: 36000,
				BaseURL:         srv.URL,
			})
			if _, _, err := client.SearchRecentPosts(context.Background(), "any", tc.requested); err != nil {
				t.Fatalf("search: %v", err)
			}
			if got := seen.Get("max_results"); got != tc.wantParam {
				t.Errorf("max_results: requested %d, want sent=%q, got %q",
					tc.requested, tc.wantParam, got)
			}
		})
	}
}

func TestNormalizeHandle_HandlesSpacesAtSignAndCase(t *testing.T) {
	cases := map[string]string{
		"@POTUS":         "potus",
		"  @SenSchumer ": "senschumer",
		"NOAT":           "noat",
		"":               "",
		"@":              "",
	}
	for in, want := range cases {
		if got := normalizeHandle(in); got != want {
			t.Errorf("normalizeHandle(%q) = %q, want %q", in, got, want)
		}
	}
}
