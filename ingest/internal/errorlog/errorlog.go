// Package errorlog wraps an slog.Handler so Error-level records are also
// mirrored durably into ops.error_log (data/pg-migrations/0009_error_log.sql,
// source='ingest'), the same dual-writer arrangement the Python side uses via
// analysis.src.common.error_log. Everything still goes to the base handler
// first; the durable write is best-effort on top of that.
package errorlog

import (
	"context"
	"database/sql"
	"encoding/json"
	"fmt"
	"log"
	"log/slog"
	"sync"
	"time"
)

const (
	// defaultComponent is used when no "component" attr is present on the
	// record or on any handler accumulated via WithAttrs.
	defaultComponent = "ingest"
	// insertTimeout bounds the durable write so a slow/unreachable database
	// never stalls the caller's logging call.
	insertTimeout = 5 * time.Second
	// rateCapWindow/rateCapMax bound durable writes per process to
	// rateCapMax per rolling window, matching the 0009 migration's writer
	// contract. Past the cap, records still reach the base handler (stderr)
	// — only the durable mirror stops.
	rateCapWindow = time.Hour
	rateCapMax    = 200
)

// Handler is an slog.Handler that delegates every record to a base handler
// and, for Error-level records, additionally inserts a row into
// ops.error_log. The zero value is not usable; construct with NewHandler. A
// nil *Handler or a Handler with a nil db is safe to call Handle/Enabled/
// WithAttrs/WithGroup on.
type Handler struct {
	base    slog.Handler
	db      *sql.DB
	attrs   []slog.Attr
	limiter *rateLimiter
}

// rateLimiter caps durable writes to rateCapMax per rolling rateCapWindow,
// shared across every Handler derived from the same NewHandler call (via
// WithAttrs/WithGroup) since they all mirror into the same table.
type rateLimiter struct {
	mu          sync.Mutex
	windowStart time.Time
	count       int
	notifiedCap bool
}

// allow reports whether a durable write should proceed. capJustReached is
// true exactly once per window, on the write that pushes the count past
// rateCapMax — the caller uses it to write a single "cap reached" row.
func (r *rateLimiter) allow() (ok bool, capJustReached bool) {
	r.mu.Lock()
	defer r.mu.Unlock()

	now := time.Now()
	if r.windowStart.IsZero() || now.Sub(r.windowStart) >= rateCapWindow {
		r.windowStart = now
		r.count = 0
		r.notifiedCap = false
	}

	if r.count >= rateCapMax {
		if !r.notifiedCap {
			r.notifiedCap = true
			return false, true
		}
		return false, false
	}

	r.count++
	return true, false
}

// NewHandler wraps base so Error-level records are additionally mirrored
// into ops.error_log via db. db may be nil (e.g. before the database is
// constructed, or in tests) — the durable mirror is then simply skipped.
func NewHandler(base slog.Handler, db *sql.DB) *Handler {
	return &Handler{base: base, db: db, limiter: &rateLimiter{}}
}

// Enabled delegates to the base handler.
func (h *Handler) Enabled(ctx context.Context, level slog.Level) bool {
	if h == nil || h.base == nil {
		return true
	}
	return h.base.Enabled(ctx, level)
}

// Handle delegates to the base handler first, then — for Error-level
// records, when db is configured — best-effort inserts a durable copy. The
// durable write never fails Handle: any error inserting is reported to
// stderr and swallowed.
func (h *Handler) Handle(ctx context.Context, record slog.Record) error {
	if h == nil || h.base == nil {
		return nil
	}

	err := h.base.Handle(ctx, record)

	if h.db != nil && record.Level >= slog.LevelError {
		h.mirror(ctx, record)
	}

	return err
}

// WithAttrs returns a new Handler carrying attrs both into the base handler
// and into this handler's own accumulated attrs, so a later Handle call can
// still find a "component" attr set earlier via slog.With(...) rather than
// only ones attached at the call site.
func (h *Handler) WithAttrs(attrs []slog.Attr) slog.Handler {
	if h == nil {
		return h
	}
	next := &Handler{
		db:      h.db,
		limiter: h.limiter,
		attrs:   append(append([]slog.Attr{}, h.attrs...), attrs...),
	}
	if h.base != nil {
		next.base = h.base.WithAttrs(attrs)
	}
	return next
}

// WithGroup delegates to the base handler. Grouped attrs are not
// group-namespaced in the durable context column (they flatten in same as
// ungrouped attrs) — nothing in this codebase's call sites uses groups, so
// this is a deliberate simplification, not a gap.
func (h *Handler) WithGroup(name string) slog.Handler {
	if h == nil {
		return h
	}
	next := &Handler{
		db:      h.db,
		limiter: h.limiter,
		attrs:   append([]slog.Attr{}, h.attrs...),
	}
	if h.base != nil {
		next.base = h.base.WithGroup(name)
	}
	return next
}

// mirror inserts one ops.error_log row for record. Never panics or
// propagates an error to the caller: any failure (rate cap, DB error) is
// reported to stderr via the standard logger and dropped.
func (h *Handler) mirror(ctx context.Context, record slog.Record) {
	allowed, capJustReached := h.limiter.allow()
	if capJustReached {
		h.insert(ctx, "ingest: error_log rate cap reached ("+fmt.Sprint(rateCapMax)+"/hr) — further errors this window are stderr-only", defaultComponent, nil)
		return
	}
	if !allowed {
		return
	}

	component, contextAttrs := extractAttrs(h.attrs, record)
	h.insert(ctx, record.Message, component, contextAttrs)
}

// insert performs the actual write, wrapped so any failure is stderr-only.
func (h *Handler) insert(ctx context.Context, message, component string, contextAttrs map[string]any) {
	insertCtx, cancel := timeoutContext(ctx)
	defer cancel()

	var contextJSON any
	if len(contextAttrs) > 0 {
		encoded, err := json.Marshal(contextAttrs)
		if err != nil {
			log.Printf("errorlog: marshal context failed: %v", err)
		} else {
			contextJSON = string(encoded)
		}
	}

	_, err := h.db.ExecContext(insertCtx, `
		INSERT INTO ops.error_log (source, component, message, context)
		VALUES ('ingest', $1, $2, $3::jsonb)
	`, component, message, contextJSON)
	if err != nil {
		log.Printf("errorlog: insert ops.error_log failed: %v", err)
	}
}

// timeoutContext bounds the insert to insertTimeout, falling back to
// context.Background if ctx is nil (Handle is always called with a non-nil
// ctx in practice, but slog's interface does not forbid nil).
func timeoutContext(ctx context.Context) (context.Context, context.CancelFunc) {
	if ctx == nil {
		ctx = context.Background()
	}
	return context.WithTimeout(ctx, insertTimeout)
}

// extractAttrs walks accumulated handler attrs (from WithAttrs) followed by
// the record's own attrs, pulling out "component" (last one wins, matching
// slog's own duplicate-key convention) and collecting everything else into a
// context map for the JSONB column.
func extractAttrs(handlerAttrs []slog.Attr, record slog.Record) (component string, contextAttrs map[string]any) {
	component = defaultComponent
	contextAttrs = make(map[string]any)

	consume := func(a slog.Attr) bool {
		if a.Key == "component" {
			component = a.Value.String()
			return true
		}
		contextAttrs[a.Key] = attrValue(a.Value)
		return true
	}

	for _, a := range handlerAttrs {
		consume(a)
	}
	record.Attrs(consume)

	return component, contextAttrs
}

// attrValue converts an slog.Value to a JSON-safe representation on a
// best-effort basis: errors become their .Error() string, durations their
// String() form, and everything else passes through as-is for
// encoding/json to handle. If the resulting map ever fails to marshal
// (unlikely for these kinds), insert drops the context column to NULL
// rather than losing the row.
func attrValue(v slog.Value) any {
	switch v.Kind() {
	case slog.KindDuration:
		return v.Duration().String()
	case slog.KindTime:
		return v.Time().Format(time.RFC3339)
	case slog.KindAny:
		if err, ok := v.Any().(error); ok {
			return err.Error()
		}
		return v.Any()
	default:
		return v.Any()
	}
}
