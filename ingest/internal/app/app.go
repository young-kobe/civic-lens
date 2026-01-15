// Package app provides application bootstrapping and shared dependencies.
package app

import (
	"context"
	"fmt"
	"os"
	"os/signal"
	"syscall"

	"github.com/young-kobe/civic-lens/ingest/internal/config"
	"github.com/young-kobe/civic-lens/ingest/internal/frontier"
	"github.com/young-kobe/civic-lens/ingest/internal/httpclient"
	"github.com/young-kobe/civic-lens/ingest/internal/robots"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/rawstore"
)

// App holds all shared application dependencies.
type App struct {
	Config   *config.Config
	Database *db.DB
	Frontier *frontier.Frontier
	Fetcher  *httpclient.Fetcher
	RawStore *rawstore.Store
	Robots   *robots.Checker
}

// Options for initializing the application.
type Options struct {
	ConfigPath string
	DBPath     string
	// RequireRawStore enables raw content storage (for crawl/reddit commands)
	RequireRawStore bool
	// RequireFetcher enables HTTP fetcher (for crawl/ingest commands)
	RequireFetcher bool
	// RequireRobots enables robots.txt checker (for crawl command)
	RequireRobots bool
}

// New creates a new App with all required dependencies.
func New(ctx context.Context, opts Options) (*App, error) {
	cfg, err := config.Load(opts.ConfigPath)
	if err != nil {
		return nil, fmt.Errorf("load config: %w", err)
	}

	database, err := db.Open(opts.DBPath)
	if err != nil {
		return nil, fmt.Errorf("open database: %w", err)
	}

	if err := database.Migrate(ctx); err != nil {
		database.Close()
		return nil, fmt.Errorf("migrate database: %w", err)
	}

	app := &App{
		Config:   cfg,
		Database: database,
		Frontier: frontier.New(database, cfg.Crawl.MaxRetries),
	}

	if opts.RequireFetcher {
		app.Fetcher = httpclient.New(httpclient.Config{
			UserAgent:      cfg.Crawl.UserAgent,
			RequestTimeout: cfg.Crawl.RequestTimeout,
			MaxRetries:     cfg.Crawl.MaxRetries,
			RatePerSec:     cfg.Crawl.RateLimitPerSec,
			MaxRedirects:   cfg.Crawl.MaxRedirects,
		})
	}

	if opts.RequireRawStore {
		store, err := rawstore.New(cfg.Database.RawDir)
		if err != nil {
			database.Close()
			return nil, fmt.Errorf("init raw store: %w", err)
		}
		app.RawStore = store
	}

	if opts.RequireRobots {
		app.Robots = robots.New(cfg.Crawl.UserAgent)
	}

	return app, nil
}

// Close releases all resources.
func (a *App) Close() error {
	if a.Database != nil {
		return a.Database.Close()
	}
	return nil
}

// NewDBOnly creates an App with only database access (for simple commands like migrate).
func NewDBOnly(ctx context.Context, dbPath string) (*App, error) {
	database, err := db.Open(dbPath)
	if err != nil {
		return nil, fmt.Errorf("open database: %w", err)
	}

	return &App{
		Database: database,
	}, nil
}

// SignalContext returns a context that is canceled on SIGINT/SIGTERM.
func SignalContext(parent context.Context) (context.Context, context.CancelFunc) {
	ctx, cancel := context.WithCancel(parent)

	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, syscall.SIGINT, syscall.SIGTERM)

	go func() {
		select {
		case <-sigCh:
			fmt.Println("\nShutting down gracefully...")
			cancel()
		case <-ctx.Done():
		}
		signal.Stop(sigCh)
	}()

	return ctx, cancel
}
