package runner

import (
	"context"

	"github.com/young-kobe/civic-lens/ingest/internal/model"
)

// insertPostPostgres upserts one row into raw.reddit_posts. Unlike x_posts,
// this table carries no provenance column that must survive a re-insert (no
// is_official_tier equivalent), so a full-column upsert is safe.
func (rr *RedditRunner) insertPostPostgres(ctx context.Context, post model.RedditPost) error {
	_, err := rr.app.Database.Conn().ExecContext(ctx, `
		INSERT INTO raw.reddit_posts
			(fullname, subreddit, created_utc, fetched_at, title, body, score, num_comments, raw_hash, extraction_version)
		VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
		ON CONFLICT (fullname) DO UPDATE SET
			subreddit = excluded.subreddit,
			created_utc = excluded.created_utc,
			fetched_at = excluded.fetched_at,
			title = excluded.title,
			body = excluded.body,
			score = excluded.score,
			num_comments = excluded.num_comments,
			raw_hash = excluded.raw_hash,
			extraction_version = excluded.extraction_version
	`,
		post.Fullname, post.Subreddit, unixOrNil(post.CreatedUTC), unixOrNil(post.FetchedAt), post.Title,
		post.Body, post.Score, post.NumComments, post.RawHash, post.ExtractionVersion,
	)
	return err
}
