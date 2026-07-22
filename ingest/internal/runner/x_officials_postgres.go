package runner

import (
	"context"
	"database/sql"
	"errors"
	"fmt"

	"github.com/young-kobe/civic-lens/ingest/internal/model"
)

// insertOfficialPostPostgres is the officials-pass variant of
// insertPostPostgres: same columns plus is_official_tier, upserted to true on
// every write (both insert and conflict-update) since a post reaching this
// path always came from the verified-officials timeline pull.
func (xr *XRunner) insertOfficialPostPostgres(ctx context.Context, post model.XPost) error {
	query := fmt.Sprintf(`
		INSERT INTO raw.x_posts (%s, is_official_tier)
		VALUES (%s, true)
		ON CONFLICT (tweet_id) DO UPDATE SET
			%s,
			is_official_tier = excluded.is_official_tier
	`, xPostColumns, xPostValuesPlaceholders, xPostUpdateSet)
	_, err := xr.app.Database.Conn().ExecContext(ctx, query, xPostArgs(post)...)
	return err
}

// lookupCachedUserIDPostgres is the Postgres counterpart to
// lookupCachedUserID (x_officials.go), reading raw.x_users instead of
// x_users_raw with a $-placeholder. Same signature so resolveOfficialUserID
// can select between them as a plain function value.
func lookupCachedUserIDPostgres(ctx context.Context, conn *sql.DB, handle string) (string, error) {
	row := conn.QueryRowContext(ctx,
		`SELECT user_id FROM raw.x_users WHERE LOWER(username) = LOWER($1) LIMIT 1`,
		handle,
	)
	var id string
	switch err := row.Scan(&id); {
	case errors.Is(err, sql.ErrNoRows):
		return "", nil
	case err != nil:
		return "", err
	}
	return id, nil
}
