package runner

import (
	"context"
	"errors"
	"fmt"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/extract/x"
	"github.com/young-kobe/civic-lens/ingest/internal/model"
)

// OfficialsResult summarises a single officials-pull pass.
type OfficialsResult struct {
	HandlesAttempted int
	HandlesSucceeded int
	HandlesFailed    int
	HandlesSkipped   int // budget exhausted before reaching them
	PostsIngested    int
	UsersIngested    int
}

// runOfficialsPass walks every handle in data/verified_officials.yaml and
// pulls that account's recent timeline. Posts are persisted with
// is_official_tier=1 so downstream stages don't need to re-classify them.
//
// Three guarantees:
//
//   - Failures are per-account. A handle that fails resolution (suspended,
//     renamed) or whose timeline 404s logs and skips; remaining accounts
//     still run.
//   - Budget priority. Officials run BEFORE topic queries (XRunner.Run
//     orders this) so a tight month never starves the verified-officials
//     surface, which is the highest-signal signal we collect.
//   - Resolved IDs are cached. The X user-by-username endpoint is billed
//     per call; once a handle's user_id lands in x_users_raw we use the
//     cached row on subsequent runs and only call the lookup endpoint for
//     handles we haven't seen before.
func (xr *XRunner) runOfficialsPass(
	ctx context.Context,
	budget *XBudgetTracker,
	officialsPath string,
	maxTweetsPerAccount int,
) (*OfficialsResult, error) {
	officials, err := x.LoadVerifiedOfficials(officialsPath)
	if err != nil {
		return nil, fmt.Errorf("load verified officials: %w", err)
	}
	if len(officials) == 0 {
		fmt.Printf("Officials pass skipped: %s missing or empty\n", officialsPath)
		return &OfficialsResult{}, nil
	}

	res := &OfficialsResult{}
	now := time.Now().Unix()

	// Collect every distinct handle (primary + also_handles), preserving the
	// editorial order so the log lines read top-down through the YAML.
	type handleJob struct {
		official x.VerifiedOfficial
		handle   string
	}
	jobs := make([]handleJob, 0, len(officials)*2)
	seen := make(map[string]struct{})
	for _, off := range officials {
		for _, h := range off.AllHandles() {
			if _, dup := seen[h]; dup {
				continue
			}
			seen[h] = struct{}{}
			jobs = append(jobs, handleJob{official: off, handle: h})
		}
	}

	fmt.Printf("Officials pass: %d handles across %d officials\n", len(jobs), len(officials))

	for _, job := range jobs {
		res.HandlesAttempted++

		if budget.OverBudget() {
			res.HandlesSkipped++
			fmt.Printf("  @%s SKIPPED — budget exhausted\n", job.handle)
			continue
		}

		userID, hydratedUser, err := xr.resolveOfficialUserID(ctx, job.handle, budget)
		if err != nil {
			res.HandlesFailed++
			fmt.Printf("  @%s lookup failed: %v\n", job.handle, err)
			continue
		}

		if hydratedUser != nil {
			hydratedUser.FetchedAt = now
			if err := xr.insertUser(ctx, *hydratedUser); err != nil {
				fmt.Printf("  @%s user insert error: %v\n", job.handle, err)
			} else {
				res.UsersIngested++
			}
		}

		if budget.OverBudget() {
			res.HandlesSkipped++
			fmt.Printf("  @%s SKIPPED post-pull — budget exhausted\n", job.handle)
			continue
		}

		resp, rawJSON, err := xr.client.UserTimeline(ctx, userID, maxTweetsPerAccount)
		if err != nil {
			res.HandlesFailed++
			fmt.Printf("  @%s timeline failed: %v\n", job.handle, err)
			continue
		}

		hash, err := xr.app.RawStore.Store(ctx, rawJSON, ".json")
		if err != nil {
			// Never persist official posts with an empty raw_hash — that
			// breaks A6/A7 traceability for the highest-signal surface we
			// collect. Count the handle as failed and move on.
			res.HandlesFailed++
			fmt.Printf("  @%s raw store error: %v\n", job.handle, err)
			continue
		}
		posts, hydrated := x.ToModels(resp)

		// Persist budget BEFORE inserts so a DB error on one timeline
		// doesn't drift the ceiling away from reality.
		if err := budget.Record(ctx, len(posts), len(hydrated)); err != nil {
			fmt.Printf("  @%s budget record error: %v\n", job.handle, err)
		}

		insertedHere := 0
		for _, post := range posts {
			post.FetchedAt = now
			post.RawHash = hash
			post.ExtractionVersion = "1.0"
			if err := xr.insertOfficialPost(ctx, post); err != nil {
				fmt.Printf("  @%s post insert error: %v\n", job.handle, err)
				continue
			}
			insertedHere++
		}
		res.PostsIngested += insertedHere

		for _, user := range hydrated {
			user.FetchedAt = now
			user.RawHash = hash
			if err := xr.insertUser(ctx, user); err != nil {
				fmt.Printf("  @%s user expansion insert error: %v\n", job.handle, err)
			} else {
				res.UsersIngested++
			}
		}

		res.HandlesSucceeded++
		fmt.Printf("  @%s OK: %d posts (raw: %s)\n", job.handle, insertedHere, safePrefix(hash, 8))
	}

	return res, nil
}

// resolveOfficialUserID returns the X numeric user_id for a handle, using
// the x_users_raw cache when available and falling back to the API
// user-by-username endpoint otherwise. When a fresh API lookup happens, the
// returned XUser carries the hydrated profile so the runner can persist it
// in the same pass (saves a separate insert and keeps verified/follower
// counts current). Lookup hits cost one user-resource against the budget.
func (xr *XRunner) resolveOfficialUserID(
	ctx context.Context,
	handle string,
	budget *XBudgetTracker,
) (string, *model.XUser, error) {
	if cachedID, err := lookupCachedUserIDPostgres(ctx, xr.app.Database.Conn(), handle); err != nil {
		return "", nil, fmt.Errorf("cache lookup: %w", err)
	} else if cachedID != "" {
		return cachedID, nil, nil
	}

	if budget.OverBudget() {
		return "", nil, errors.New("budget exhausted before user lookup")
	}

	userData, _, err := xr.client.UserByUsername(ctx, handle)
	if err != nil {
		return "", nil, err
	}
	// One user-resource billed against the monthly budget.
	if err := budget.Record(ctx, 0, 1); err != nil {
		fmt.Printf("  @%s budget record error (user lookup): %v\n", handle, err)
	}

	// Convert to model.XUser so the caller can persist via the existing
	// insertUser path. Unset fields (e.g. CreatedAt parse failures) are
	// tolerated upstream; this matches what ToModels() does for search.
	user := model.XUser{
		UserID:          userData.ID,
		Username:        userData.Username,
		Name:            userData.Name,
		Location:        userData.Location,
		Description:     userData.Description,
		Verified:        userData.Verified,
		VerifiedType:    userData.VerifiedType,
		Protected:       userData.Protected,
		ProfileImageURL: userData.ProfileImageURL,
	}
	if userData.PublicMetrics != nil {
		user.FollowersCount = userData.PublicMetrics.FollowersCount
		user.FollowingCount = userData.PublicMetrics.FollowingCount
		user.TweetCount = userData.PublicMetrics.TweetCount
		user.ListedCount = userData.PublicMetrics.ListedCount
	}
	if userData.CreatedAt != "" {
		if t, err := time.Parse(time.RFC3339, userData.CreatedAt); err == nil {
			user.CreatedAt = t.Unix()
		}
	}
	return userData.ID, &user, nil
}

// insertOfficialPost is the officials-pass variant of insertPost: same
// columns plus is_official_tier=1. Kept as a separate method so the
// search-query path stays completely untouched (its callers don't even
// know the column exists).
func (xr *XRunner) insertOfficialPost(ctx context.Context, post model.XPost) error {
	return xr.insertOfficialPostPostgres(ctx, post)
}

func safePrefix(s string, n int) string {
	if len(s) < n {
		return s
	}
	return s[:n]
}
