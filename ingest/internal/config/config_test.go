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
