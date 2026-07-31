package errorlog

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"log/slog"
	"os"
	"testing"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
)

// --- Ungated tests: no CIVIC_TEST_POSTGRES_DSN required. ---

// TestHandlerNilDBNoPanic verifies a Handler with a nil db still delegates
// every record to the base handler and never panics on an Error record —
// the common case before the database is constructed (or, per the design,
// any time the durable mirror is unavailable).
func TestHandlerNilDBNoPanic(t *testing.T) {
	var buf bytes.Buffer
	base := slog.NewTextHandler(&buf, nil)
	h := NewHandler(base, nil)
	logger := slog.New(h)

	logger.Error("boom", "component", "test.thing", "url", "https://example.com")

	out := buf.String()
	if !contains(out, "boom") || !contains(out, "test.thing") {
		t.Fatalf("expected base handler output to contain message and component, got: %s", out)
	}
}

// TestHandlerNilReceiverNoPanic verifies a nil *Handler is safe to call —
// the design explicitly requires this even though production code never
// constructs one this way.
func TestHandlerNilReceiverNoPanic(t *testing.T) {
	var h *Handler

	if !h.Enabled(context.Background(), slog.LevelError) {
		t.Fatal("nil Handler.Enabled should default to true")
	}

	r := slog.NewRecord(time.Now(), slog.LevelError, "boom", 0)
	if err := h.Handle(context.Background(), r); err != nil {
		t.Fatalf("nil Handler.Handle should return nil, got: %v", err)
	}

	if got := h.WithAttrs([]slog.Attr{slog.String("component", "x")}); got != slog.Handler(h) {
		t.Fatal("nil Handler.WithAttrs should return the same nil handler")
	}
	if got := h.WithGroup("g"); got != slog.Handler(h) {
		t.Fatal("nil Handler.WithGroup should return the same nil handler")
	}
}

// TestExtractAttrsComponent covers the three ways a "component" attr can
// reach a record — from WithAttrs (handler-level), from the call site
// (record-level), overriding, or absent entirely — and checks non-component
// attrs land in the context map instead.
func TestExtractAttrsComponent(t *testing.T) {
	newRecord := func(attrs ...slog.Attr) slog.Record {
		r := slog.NewRecord(time.Now(), slog.LevelError, "boom", 0)
		r.AddAttrs(attrs...)
		return r
	}

	t.Run("record attr only", func(t *testing.T) {
		component, ctx := extractAttrs(nil, newRecord(slog.String("component", "runner.reddit"), slog.String("url", "https://x")))
		if component != "runner.reddit" {
			t.Fatalf("component = %q, want runner.reddit", component)
		}
		if ctx["url"] != "https://x" {
			t.Fatalf("context[url] = %v, want https://x", ctx["url"])
		}
		if _, present := ctx["component"]; present {
			t.Fatal("component must not also land in the context map")
		}
	})

	t.Run("handler attr only", func(t *testing.T) {
		handlerAttrs := []slog.Attr{slog.String("component", "runner.crawl")}
		component, _ := extractAttrs(handlerAttrs, newRecord(slog.String("url", "https://x")))
		if component != "runner.crawl" {
			t.Fatalf("component = %q, want runner.crawl", component)
		}
	})

	t.Run("record attr overrides handler attr", func(t *testing.T) {
		handlerAttrs := []slog.Attr{slog.String("component", "runner.crawl")}
		component, _ := extractAttrs(handlerAttrs, newRecord(slog.String("component", "runner.crawl.override")))
		if component != "runner.crawl.override" {
			t.Fatalf("component = %q, want runner.crawl.override", component)
		}
	})

	t.Run("absent falls back to default", func(t *testing.T) {
		component, _ := extractAttrs(nil, newRecord(slog.String("url", "https://x")))
		if component != defaultComponent {
			t.Fatalf("component = %q, want %q", component, defaultComponent)
		}
	})
}

// TestRateLimiterCap verifies the rolling-window cap: exactly rateCapMax
// writes are allowed, the write that would be the (rateCapMax+1)th instead
// reports capJustReached so the caller writes a single "cap reached" row,
// and every write after that in the same window is silently disallowed.
func TestRateLimiterCap(t *testing.T) {
	r := &rateLimiter{}

	for i := 0; i < rateCapMax; i++ {
		ok, capJustReached := r.allow()
		if !ok || capJustReached {
			t.Fatalf("write %d: got (ok=%v, capJustReached=%v), want (true, false)", i, ok, capJustReached)
		}
	}

	if ok, capJustReached := r.allow(); ok || !capJustReached {
		t.Fatalf("write %d (first over cap): got (ok=%v, capJustReached=%v), want (false, true)", rateCapMax, ok, capJustReached)
	}

	if ok, capJustReached := r.allow(); ok || capJustReached {
		t.Fatalf("write %d (second over cap): got (ok=%v, capJustReached=%v), want (false, false)", rateCapMax+1, ok, capJustReached)
	}
}

func contains(haystack, needle string) bool {
	return bytes.Contains([]byte(haystack), []byte(needle))
}

// --- Postgres-gated tests. ---

// pgTestDSNEnv names the env var that opts these tests into a real Postgres
// instance — the same variable and skip convention used throughout ingest's
// gated tests (see internal/runner/postgres_integration_test.go,
// internal/frontier/frontier_postgres_test.go). Unset by default, so a plain
// `go test ./...` never touches a real database.
const pgTestDSNEnv = "CIVIC_TEST_POSTGRES_DSN"

// errorLogFixtureSQL idempotently ensures ops.error_log exists, mirroring
// data/pg-migrations/0009_error_log.sql's column set. `go test` runs each
// package with its working directory set to that package's own source
// directory, so db.Migrate (which locates migrations relative to the
// process CWD) cannot find data/pg-migrations from here — the same reason
// internal/frontier's gated tests hand-roll a fixture instead of calling
// Migrate. Only ever creates, never drops: ops is a shared schema other
// gated suites (e.g. the x budget tests) also depend on.
const errorLogFixtureSQL = `
CREATE SCHEMA IF NOT EXISTS ops;
CREATE TABLE IF NOT EXISTS ops.error_log (
	error_id        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	occurred_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
	source          TEXT NOT NULL,
	component       TEXT NOT NULL,
	message         TEXT NOT NULL,
	traceback       TEXT,
	doc_id          BIGINT,
	task            TEXT,
	pipeline_run_id BIGINT,
	context         JSONB
);
`

// openTestPostgres connects to CIVIC_TEST_POSTGRES_DSN, ensures ops.error_log
// exists, and returns the DB plus a cleanup that only closes the connection
// — skips the calling test cleanly when the env var is unset.
func openTestPostgres(t *testing.T) *db.DB {
	t.Helper()
	dsn := os.Getenv(pgTestDSNEnv)
	if dsn == "" {
		t.Skipf("%s not set; skipping Postgres integration test", pgTestDSNEnv)
	}

	database, err := db.Open(dsn)
	if err != nil {
		t.Fatalf("open postgres: %v", err)
	}
	t.Cleanup(func() { _ = database.Close() })

	if _, err := database.Conn().ExecContext(context.Background(), errorLogFixtureSQL); err != nil {
		t.Fatalf("ensure ops.error_log fixture: %v", err)
	}
	return database
}

// TestPostgresHandlerInsertsErrorRow drives a real Handler end to end: an
// Error-level record with a component attr and extra context attrs should
// land as one ops.error_log row with source='ingest' and the context attrs
// round-tripped through the JSONB column.
func TestPostgresHandlerInsertsErrorRow(t *testing.T) {
	database := openTestPostgres(t)
	ctx := context.Background()

	message := fmt.Sprintf("errorlog test row %d", time.Now().UnixNano())
	t.Cleanup(func() {
		_, _ = database.Conn().ExecContext(context.Background(),
			`DELETE FROM ops.error_log WHERE message = $1`, message)
	})

	h := NewHandler(slog.NewTextHandler(&bytes.Buffer{}, nil), database.Conn())
	logger := slog.New(h)
	logger.Error(message, "component", "runner.errorlog_test", "url", "https://example.com", "count", 3)

	var source, component, gotMessage string
	var contextRaw []byte
	row := database.Conn().QueryRowContext(ctx,
		`SELECT source, component, message, context FROM ops.error_log WHERE message = $1`, message)
	if err := row.Scan(&source, &component, &gotMessage, &contextRaw); err != nil {
		t.Fatalf("query inserted row: %v", err)
	}

	if source != "ingest" {
		t.Errorf("source = %q, want ingest", source)
	}
	if component != "runner.errorlog_test" {
		t.Errorf("component = %q, want runner.errorlog_test", component)
	}
	if gotMessage != message {
		t.Errorf("message = %q, want %q", gotMessage, message)
	}

	var contextAttrs map[string]any
	if err := json.Unmarshal(contextRaw, &contextAttrs); err != nil {
		t.Fatalf("unmarshal context: %v", err)
	}
	if contextAttrs["url"] != "https://example.com" {
		t.Errorf("context[url] = %v, want https://example.com", contextAttrs["url"])
	}
	if contextAttrs["count"] != float64(3) {
		t.Errorf("context[count] = %v, want 3", contextAttrs["count"])
	}
}

// TestPostgresHandlerClosedDBNoPanic verifies the "never fails, never
// panics" contract holds even when the db handle is already unusable — the
// insert should fail silently (stderr only) rather than surface to the
// caller.
func TestPostgresHandlerClosedDBNoPanic(t *testing.T) {
	database := openTestPostgres(t)
	conn := database.Conn()
	if err := conn.Close(); err != nil {
		t.Fatalf("close connection: %v", err)
	}

	h := NewHandler(slog.NewTextHandler(&bytes.Buffer{}, nil), conn)
	logger := slog.New(h)

	logger.Error("boom against closed db", "component", "runner.errorlog_test")
}
