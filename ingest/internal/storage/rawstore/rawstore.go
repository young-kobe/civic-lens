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
	return &Store{baseDir: baseDir}, nil
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

	// Write atomically via temp file
	tmpPath := path + ".tmp"
	if err := os.WriteFile(tmpPath, data, 0644); err != nil {
		return "", fmt.Errorf("write temp file: %w", err)
	}

	if err := os.Rename(tmpPath, path); err != nil {
		os.Remove(tmpPath) // Clean up on failure
		return "", fmt.Errorf("rename to final: %w", err)
	}

	return hashStr, nil
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
