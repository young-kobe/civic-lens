package runner

import (
	"context"
	"database/sql"
	"fmt"
	"strings"
)

// upsertRow runs an INSERT OR REPLACE for the given table/columns on the
// supplied DB handle. Centralising the boilerplate means each runner
// insert site becomes a single call, and changing the insert strategy
// (e.g. to ON CONFLICT upsert) happens in one place.
func upsertRow(ctx context.Context, conn *sql.DB, table string, columns []string, values []any) error {
	if len(columns) != len(values) {
		return fmt.Errorf("upsertRow: %d columns vs %d values", len(columns), len(values))
	}

	placeholders := strings.Repeat("?, ", len(columns))
	placeholders = placeholders[:len(placeholders)-2]

	query := fmt.Sprintf(
		"INSERT OR REPLACE INTO %s (%s) VALUES (%s)",
		table,
		strings.Join(columns, ", "),
		placeholders,
	)

	_, err := conn.ExecContext(ctx, query, values...)
	return err
}

// upsertRowOnConflict inserts a row and, on a conflict against conflictCol,
// updates only the listed columns — leaving any column NOT in `columns`
// untouched. Unlike INSERT OR REPLACE (which deletes and re-inserts, resetting
// unlisted columns to their defaults), this preserves provenance flags such as
// x_posts_raw.is_official_tier when a later pass re-ingests the same row
// without that column.
func upsertRowOnConflict(ctx context.Context, conn *sql.DB, table, conflictCol string, columns []string, values []any) error {
	if len(columns) != len(values) {
		return fmt.Errorf("upsertRowOnConflict: %d columns vs %d values", len(columns), len(values))
	}

	placeholders := strings.Repeat("?, ", len(columns))
	placeholders = placeholders[:len(placeholders)-2]

	assignments := make([]string, 0, len(columns))
	for _, col := range columns {
		if col == conflictCol {
			continue
		}
		assignments = append(assignments, col+" = excluded."+col)
	}

	query := fmt.Sprintf(
		"INSERT INTO %s (%s) VALUES (%s) ON CONFLICT(%s) DO UPDATE SET %s",
		table,
		strings.Join(columns, ", "),
		placeholders,
		conflictCol,
		strings.Join(assignments, ", "),
	)

	_, err := conn.ExecContext(ctx, query, values...)
	return err
}
