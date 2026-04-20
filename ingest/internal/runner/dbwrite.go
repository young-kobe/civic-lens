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
