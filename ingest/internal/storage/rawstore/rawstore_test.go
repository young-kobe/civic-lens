package rawstore

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"os"
	"path/filepath"
	"testing"
)

// TestStoreRoundTrip asserts content is durably written and retrievable, and
// the hash is the SHA-256 of the bytes (A5 integrity).
func TestStoreRoundTrip(t *testing.T) {
	s, err := New(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	data := []byte("hello world")
	hash, err := s.Store(context.Background(), data, ".txt")
	if err != nil {
		t.Fatalf("store: %v", err)
	}
	want := hex.EncodeToString(sha256Sum(data))
	if hash != want {
		t.Errorf("hash = %s, want %s", hash, want)
	}
	got, err := s.Retrieve(context.Background(), hash, ".txt")
	if err != nil {
		t.Fatalf("retrieve: %v", err)
	}
	if string(got) != string(data) {
		t.Errorf("retrieved %q, want %q", got, data)
	}
}

// TestStoreErrorReturnsNoHash is the I-3 mechanism at the store level: when the
// write path can't be created, Store returns an error and an empty hash. The
// runner callers now check this error and skip the item instead of persisting
// an empty raw_hash / panicking on hash[:8].
func TestStoreErrorReturnsNoHash(t *testing.T) {
	base := t.TempDir()
	s, err := New(base)
	if err != nil {
		t.Fatal(err)
	}
	data := []byte("collision")
	first2 := hex.EncodeToString(sha256Sum(data))[:2]

	// Put a regular file where Store needs to MkdirAll a subdirectory, so the
	// directory creation fails deterministically.
	blocker := filepath.Join(base, "sha256", first2)
	if err := os.WriteFile(blocker, []byte("x"), 0644); err != nil {
		t.Fatal(err)
	}

	hash, err := s.Store(context.Background(), data, ".txt")
	if err == nil {
		t.Fatal("expected error when subdirectory creation fails")
	}
	if hash != "" {
		t.Errorf("hash = %q, want empty on error", hash)
	}
}

// TestNewSweepsOrphanedTempFiles is the I-12a sweep: a .tmp left by a crashed
// Store (write succeeded, rename never ran) is removed on the next startup.
func TestNewSweepsOrphanedTempFiles(t *testing.T) {
	base := t.TempDir()
	if _, err := New(base); err != nil {
		t.Fatal(err)
	}
	orphanDir := filepath.Join(base, "sha256", "ab")
	if err := os.MkdirAll(orphanDir, 0755); err != nil {
		t.Fatal(err)
	}
	orphan := filepath.Join(orphanDir, "abcd1234.html.tmp")
	if err := os.WriteFile(orphan, []byte("half-written"), 0644); err != nil {
		t.Fatal(err)
	}

	// Re-open the store — this must sweep the orphan.
	if _, err := New(base); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(orphan); !os.IsNotExist(err) {
		t.Errorf("orphaned .tmp not swept: stat err = %v", err)
	}
}

func sha256Sum(b []byte) []byte {
	h := sha256.Sum256(b)
	return h[:]
}
