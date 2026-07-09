package config

import (
	"os"
	"path/filepath"
	"testing"
)

// writeSeedsYAML drops a minimal seeds.yaml in a temp dir and returns its
// path. Tests use this to exercise the path-resolution rules in Load
// against a real on-disk file in a known directory.
func writeSeedsYAML(t *testing.T, body string) string {
	t.Helper()
	dir := t.TempDir()
	path := filepath.Join(dir, "seeds.yaml")
	if err := os.WriteFile(path, []byte(body), 0o600); err != nil {
		t.Fatalf("write: %v", err)
	}
	return path
}

func TestLoad_OfficialsListPath_DefaultsAlongsideSeedsFile(t *testing.T) {
	// No officials_list_path key — the loader must fall back to
	// `verified_officials.yaml` resolved against the seeds.yaml directory,
	// not against the process working directory. This is the rule that
	// makes deploy work where WorkingDirectory != binary directory.
	path := writeSeedsYAML(t, `x:
  user_agent: t
`)
	cfg, err := Load(path)
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	want := filepath.Join(filepath.Dir(path), "verified_officials.yaml")
	if cfg.X.OfficialsListPath != want {
		t.Errorf("default officials path: want %q, got %q", want, cfg.X.OfficialsListPath)
	}
	if !filepath.IsAbs(cfg.X.OfficialsListPath) {
		t.Errorf("resolved officials path must be absolute, got %q", cfg.X.OfficialsListPath)
	}
}

func TestLoad_OfficialsListPath_RelativeResolvedAgainstSeedsDir(t *testing.T) {
	// When the YAML carries a relative path, the loader joins it with the
	// seeds.yaml directory — NOT the process working directory. The legacy
	// `data/verified_officials.yaml` shape (in case anyone keeps using it)
	// must therefore land in the same place.
	path := writeSeedsYAML(t, `x:
  user_agent: t
  officials_list_path: subdir/officials.yaml
`)
	cfg, err := Load(path)
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	want := filepath.Join(filepath.Dir(path), "subdir", "officials.yaml")
	if cfg.X.OfficialsListPath != want {
		t.Errorf("relative officials path: want %q, got %q", want, cfg.X.OfficialsListPath)
	}
}

// TestLoad_StrictRejectsUnknownKey is the I-12b regression: a misspelled key
// must fail loudly instead of silently falling back to a default. Before strict
// decoding, `max_concurency:` (typo) was ignored and the crawler ran with the
// default concurrency, quietly discarding the operator's intended setting.
func TestLoad_StrictRejectsUnknownKey(t *testing.T) {
	path := writeSeedsYAML(t, `crawl:
  max_concurency: 3
`)
	if _, err := Load(path); err == nil {
		t.Fatal("Load should reject an unknown/misspelled key, got nil error")
	}
}

// TestLoad_KnownKeysStillParse guards against strict mode being too strict:
// the real key shapes (including duration strings) must still load.
func TestLoad_KnownKeysStillParse(t *testing.T) {
	path := writeSeedsYAML(t, `crawl:
  max_concurrency: 3
  request_timeout: 30s
  stale_inflight_age: 10m
reddit:
  subreddits:
    - politics
`)
	cfg, err := Load(path)
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	if cfg.Crawl.MaxConcurrency != 3 {
		t.Errorf("max_concurrency = %d, want 3", cfg.Crawl.MaxConcurrency)
	}
	if cfg.Crawl.RequestTimeout.String() != "30s" {
		t.Errorf("request_timeout = %v, want 30s", cfg.Crawl.RequestTimeout)
	}
}

func TestLoad_OfficialsListPath_AbsoluteIsPreserved(t *testing.T) {
	// Absolute paths in the YAML pass through untouched — an operator who
	// wants to point at a system-wide registry shouldn't have it mangled
	// by the loader.
	abs := filepath.Join(t.TempDir(), "system-officials.yaml")
	path := writeSeedsYAML(t, `x:
  user_agent: t
  officials_list_path: `+abs+`
`)
	cfg, err := Load(path)
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	if cfg.X.OfficialsListPath != abs {
		t.Errorf("absolute path mangled: want %q, got %q", abs, cfg.X.OfficialsListPath)
	}
}
