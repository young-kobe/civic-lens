package runner

import (
	"context"
	"fmt"

	"github.com/young-kobe/civic-lens/ingest/internal/model"
)

// xPostColumns and xPostUpdateSet are the raw.x_posts columns/assignments
// shared by both upsert paths (search-query and officials-pass). They
// deliberately exclude is_official_tier: insertPostPostgres (search-query
// path) never lists it, preserving whatever tier flag an earlier
// officials-pass insert set (I-6). insertOfficialPostPostgres
// (x_officials_postgres.go) appends it to both.
const xPostColumns = `tweet_id, author_id, conversation_id, created_at, fetched_at,
		text, lang, retweet_count, reply_count, like_count, quote_count,
		place_id, place_country_code, place_full_name,
		context_annotations, in_reply_to_user_id,
		referenced_tweet_id, referenced_tweet_type,
		raw_hash, extraction_version`

const xPostValuesPlaceholders = `$1, $2, $3, $4, $5,
		$6, $7, $8, $9, $10, $11,
		$12, $13, $14,
		$15::jsonb, $16,
		$17, $18,
		$19, $20`

const xPostUpdateSet = `author_id = excluded.author_id,
			conversation_id = excluded.conversation_id,
			created_at = excluded.created_at,
			fetched_at = excluded.fetched_at,
			text = excluded.text,
			lang = excluded.lang,
			retweet_count = excluded.retweet_count,
			reply_count = excluded.reply_count,
			like_count = excluded.like_count,
			quote_count = excluded.quote_count,
			place_id = excluded.place_id,
			place_country_code = excluded.place_country_code,
			place_full_name = excluded.place_full_name,
			context_annotations = excluded.context_annotations,
			in_reply_to_user_id = excluded.in_reply_to_user_id,
			referenced_tweet_id = excluded.referenced_tweet_id,
			referenced_tweet_type = excluded.referenced_tweet_type,
			raw_hash = excluded.raw_hash,
			extraction_version = excluded.extraction_version`

// xPostArgs is the argument list shared by both raw.x_posts upsert paths, in
// the same order as xPostColumns/xPostValuesPlaceholders.
func xPostArgs(post model.XPost) []any {
	return []any{
		post.TweetID, post.AuthorID, post.ConversationID, unixTime(post.CreatedAt), unixTime(post.FetchedAt),
		post.Text, post.Lang, post.RetweetCount, post.ReplyCount, post.LikeCount, post.QuoteCount,
		post.PlaceID, post.PlaceCountryCode, post.PlaceFullName,
		nullableJSON(post.ContextAnnotationsJSON), post.InReplyToUserID,
		post.ReferencedTweetID, post.ReferencedTweetType,
		post.RawHash, post.ExtractionVersion,
	}
}

// insertPostPostgres is the search-query path: is_official_tier is never in
// the column list, so ON CONFLICT leaves an existing officials-pass tier flag
// untouched (see xPostColumns).
func (xr *XRunner) insertPostPostgres(ctx context.Context, post model.XPost) error {
	query := fmt.Sprintf(`
		INSERT INTO raw.x_posts (%s)
		VALUES (%s)
		ON CONFLICT (tweet_id) DO UPDATE SET
			%s
	`, xPostColumns, xPostValuesPlaceholders, xPostUpdateSet)
	_, err := xr.app.Database.Conn().ExecContext(ctx, query, xPostArgs(post)...)
	return err
}

// insertUserPostgres upserts one row into raw.x_users. verified/protected are
// real BOOLEAN columns, so user.Verified/user.Protected pass straight
// through.
func (xr *XRunner) insertUserPostgres(ctx context.Context, user model.XUser) error {
	_, err := xr.app.Database.Conn().ExecContext(ctx, `
		INSERT INTO raw.x_users (
			user_id, username, name, location, description, created_at,
			followers_count, following_count, tweet_count, listed_count,
			verified, verified_type, profile_image_url, protected,
			fetched_at, raw_hash
		) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16)
		ON CONFLICT (user_id) DO UPDATE SET
			username = excluded.username,
			name = excluded.name,
			location = excluded.location,
			description = excluded.description,
			created_at = excluded.created_at,
			followers_count = excluded.followers_count,
			following_count = excluded.following_count,
			tweet_count = excluded.tweet_count,
			listed_count = excluded.listed_count,
			verified = excluded.verified,
			verified_type = excluded.verified_type,
			profile_image_url = excluded.profile_image_url,
			protected = excluded.protected,
			fetched_at = excluded.fetched_at,
			raw_hash = excluded.raw_hash
	`,
		user.UserID, user.Username, user.Name, user.Location, user.Description, unixOrNil(user.CreatedAt),
		user.FollowersCount, user.FollowingCount, user.TweetCount, user.ListedCount,
		user.Verified, user.VerifiedType, user.ProfileImageURL, user.Protected,
		unixTime(user.FetchedAt), user.RawHash,
	)
	return err
}
