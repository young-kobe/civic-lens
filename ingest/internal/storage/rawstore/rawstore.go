package rawstore

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"os"
	"path/filepath"
)

// Store provides content-addressed storage for raw fetched content.
type Store struct {
	baseDir string
}

// New creates a new RawStore at the given directory.
func New(baseDir string) (*Store, error) {
	// Create the sha256 subdirectory
	dir := filepath.Join(baseDir, "sha256")
	if err := os.MkdirAll(dir, 0755); err != nil {
		return nil, fmt.Errorf("create raw store directory: %w", err)
	}
	s := &Store{baseDir: baseDir}
	// Sweep .tmp files left behind by a Store() that crashed between write and
	// rename. They are never referenced by any hash, so removing them on
	// startup keeps the store from accumulating orphans over time.
	if err := s.sweepTemp(dir); err != nil {
		return nil, fmt.Errorf("sweep orphaned temp files: %w", err)
	}
	return s, nil
}

// sweepTemp removes orphaned *.tmp files under the sha256 tree.
func (s *Store) sweepTemp(shaDir string) error {
	return filepath.WalkDir(shaDir, func(path string, d os.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if !d.IsDir() && filepath.Ext(path) == ".tmp" {
			if rmErr := os.Remove(path); rmErr != nil && !os.IsNotExist(rmErr) {
				return rmErr
			}
		}
		return nil
	})
}

// Store writes content to disk and returns its SHA256 hash.
// The extension should include the dot, e.g. ".html" or ".json".
// This operation is idempotent: storing the same content returns the same hash.
func (s *Store) Store(ctx context.Context, data []byte, ext string) (string, error) {
	// Compute hash
	h := sha256.Sum256(data)
	hashStr := hex.EncodeToString(h[:])

	// Build path: sha256/<first2>/<hash>.ext
	subdir := filepath.Join(s.baseDir, "sha256", hashStr[:2])
	if err := os.MkdirAll(subdir, 0755); err != nil {
		return "", fmt.Errorf("create subdirectory: %w", err)
	}

	path := filepath.Join(subdir, hashStr+ext)

	// Check if file already exists (idempotent)
	if _, err := os.Stat(path); err == nil {
		return hashStr, nil
	}

	// Write atomically via temp file, fsync'd before the rename so a
	// power-loss can never leave a referenced hash pointing at a truncated
	// file (A5 integrity). Without the fsync the rename can be persisted
	// ahead of the data on some filesystems.
	tmpPath := path + ".tmp"
	if err := writeFileSync(tmpPath, data); err != nil {
		os.Remove(tmpPath)
		return "", err
	}

	if err := os.Rename(tmpPath, path); err != nil {
		os.Remove(tmpPath) // Clean up on failure
		return "", fmt.Errorf("rename to final: %w", err)
	}

	return hashStr, nil
}

// writeFileSync writes data to path and fsyncs the file before returning, so
// the bytes are durable on disk prior to the caller's rename.
func writeFileSync(path string, data []byte) error {
	f, err := os.OpenFile(path, os.O_WRONLY|os.O_CREATE|os.O_TRUNC, 0644)
	if err != nil {
		return fmt.Errorf("create temp file: %w", err)
	}
	if _, err := f.Write(data); err != nil {
		f.Close()
		return fmt.Errorf("write temp file: %w", err)
	}
	if err := f.Sync(); err != nil {
		f.Close()
		return fmt.Errorf("fsync temp file: %w", err)
	}
	if err := f.Close(); err != nil {
		return fmt.Errorf("close temp file: %w", err)
	}
	return nil
}

// Retrieve reads content by its hash. Returns os.ErrNotExist if not found.
func (s *Store) Retrieve(ctx context.Context, hash string, ext string) ([]byte, error) {
	if len(hash) < 2 {
		return nil, fmt.Errorf("invalid hash")
	}
	path := filepath.Join(s.baseDir, "sha256", hash[:2], hash+ext)
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	return data, nil
}

// Path returns the filesystem path for a given hash.
func (s *Store) Path(hash string, ext string) string {
	if len(hash) < 2 {
		return ""
	}
	return filepath.Join(s.baseDir, "sha256", hash[:2], hash+ext)
}
