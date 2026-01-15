package runner

import (
	"context"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/frontier"
)

// RequeueOptions configures the requeue runner.
type RequeueOptions struct {
	StaleAge time.Duration
}

// RequeueResult holds requeue statistics.
type RequeueResult struct {
	Recovered int64
}

// RunRequeueStale recovers stale in-flight items.
func RunRequeueStale(ctx context.Context, front *frontier.Frontier, opts RequeueOptions) (*RequeueResult, error) {
	recovered, err := front.RecoverStale(ctx, opts.StaleAge)
	if err != nil {
		return nil, err
	}

	return &RequeueResult{
		Recovered: recovered,
	}, nil
}
