package frontier

import (
	"context"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/model"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
)

func TestFrontierBasicOperations(t *testing.T) {
	// Create temp database
	tmpFile, err := os.CreateTemp("", "frontier_test_*.db")
	if err != nil {
		t.Fatal(err)
	}
	defer os.Remove(tmpFile.Name())
	tmpFile.Close()

	database, err := db.Open(tmpFile.Name())
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	// Create test migrations dir and copy from project root
	projectRoot, _ := filepath.Abs("../../..")
	srcMigrationsDir := filepath.Join(projectRoot, "data", "migrations")
	
	migrationsDir := filepath.Join(filepath.Dir(tmpFile.Name()), "migrations")
	os.MkdirAll(migrationsDir, 0755)
	defer os.RemoveAll(migrationsDir)
	
	files, _ := os.ReadDir(srcMigrationsDir)
	for _, f := range files {
		if filepath.Ext(f.Name()) == ".sql" {
			content, _ := os.ReadFile(filepath.Join(srcMigrationsDir, f.Name()))
			os.WriteFile(filepath.Join(migrationsDir, f.Name()), content, 0644)
		}
	}

	ctx := context.Background()
	if err := database.Migrate(ctx); err != nil {
		t.Fatal(err)
	}

	f := New(database, 3)

	// Test PushLinks - should add unique URLs
	added1, err := f.PushLinks(ctx, []string{
		"https://example.com/page1",
		"https://example.com/page2",
	}, 5)
	if err != nil {
		t.Fatal(err)
	}
	if added1 != 2 {
		t.Errorf("PushLinks added %d, want 2", added1)
	}

	// Test duplicate rejection
	added2, err := f.PushLinks(ctx, []string{
		"https://example.com/page1", // duplicate
		"https://example.com/page3",
	}, 5)
	if err != nil {
		t.Fatal(err)
	}
	if added2 != 1 {
		t.Errorf("PushLinks added %d, want 1 (should reject duplicate)", added2)
	}

	// Test ClaimItems
	items, err := f.ClaimItems(ctx, 10)
	if err != nil {
		t.Fatal(err)
	}
	if len(items) != 3 {
		t.Errorf("ClaimItems got %d, want 3", len(items))
	}

	// Verify state transition
	for _, item := range items {
		if item.State != model.StateInflight {
			t.Errorf("Claimed item state = %d, want INFLIGHT", item.State)
		}
	}

	// ClaimItems again should return nothing (all inflight)
	items2, err := f.ClaimItems(ctx, 10)
	if err != nil {
		t.Fatal(err)
	}
	if len(items2) != 0 {
		t.Errorf("ClaimItems second call got %d, want 0", len(items2))
	}

	// Test MarkDone
	items[0].HTTPStatus = 200
	items[0].ContentSHA256 = "abc123"
	if err := f.MarkDone(ctx, items[0]); err != nil {
		t.Fatal(err)
	}

	// Test MarkFailed with retry
	if err := f.MarkFailed(ctx, items[1], "timeout", false); err != nil {
		t.Fatal(err)
	}

	// Test Stats
	queued, inflight, done, failed, err := f.Stats(ctx)
	if err != nil {
		t.Fatal(err)
	}
	// 1 done, 1 queued (retry), 1 inflight
	if done != 1 {
		t.Errorf("Stats done = %d, want 1", done)
	}
	if queued != 1 {
		t.Errorf("Stats queued = %d, want 1", queued)
	}
	if inflight != 1 {
		t.Errorf("Stats inflight = %d, want 1", inflight)
	}
	if failed != 0 {
		t.Errorf("Stats failed = %d, want 0", failed)
	}
}

func TestFrontierRecoverStale(t *testing.T) {
	tmpFile, err := os.CreateTemp("", "frontier_stale_test_*.db")
	if err != nil {
		t.Fatal(err)
	}
	defer os.Remove(tmpFile.Name())
	tmpFile.Close()

	database, err := db.Open(tmpFile.Name())
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	// Create test migrations dir and copy from project root
	projectRoot, _ := filepath.Abs("../../..")
	srcMigrationsDir := filepath.Join(projectRoot, "data", "migrations")
	
	migrationsDir := filepath.Join(filepath.Dir(tmpFile.Name()), "migrations")
	os.MkdirAll(migrationsDir, 0755)
	defer os.RemoveAll(migrationsDir)
	
	files, _ := os.ReadDir(srcMigrationsDir)
	for _, f := range files {
		if filepath.Ext(f.Name()) == ".sql" {
			content, _ := os.ReadFile(filepath.Join(srcMigrationsDir, f.Name()))
			os.WriteFile(filepath.Join(migrationsDir, f.Name()), content, 0644)
		}
	}

	ctx := context.Background()
	if err := database.Migrate(ctx); err != nil {
		t.Fatal(err)
	}

	f := New(database, 3)

	// Add and claim items
	f.PushLinks(ctx, []string{"https://example.com/stale"}, 0)
	items, _ := f.ClaimItems(ctx, 1)
	if len(items) != 1 {
		t.Fatal("Expected 1 claimed item")
	}

	// Manually set inflight_at to 1 hour ago
	oldTime := time.Now().Add(-1 * time.Hour).Unix()
	database.Conn().ExecContext(ctx, "UPDATE pages SET inflight_at = ? WHERE url_canon = ?",
		oldTime, items[0].URLCanon)

	// Recover stale with 10 minute threshold
	recovered, err := f.RecoverStale(ctx, 10*time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	if recovered != 1 {
		t.Errorf("RecoverStale recovered %d, want 1", recovered)
	}

	// Item should be claimable again
	items2, _ := f.ClaimItems(ctx, 1)
	if len(items2) != 1 {
		t.Errorf("After recovery, ClaimItems got %d, want 1", len(items2))
	}
}
