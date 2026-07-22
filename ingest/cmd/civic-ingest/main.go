package main

import (
	"context"
	"fmt"
	"log"
	"os"
	"time"

	"github.com/spf13/cobra"
	"github.com/young-kobe/civic-lens/ingest/internal/app"
	"github.com/young-kobe/civic-lens/ingest/internal/runner"
)

var (
	cfgPath string
	dbPath  string
)

func main() {
	rootCmd := &cobra.Command{
		Use:   "civic-ingest",
		Short: "Civic Lens ingestion tool",
	}

	rootCmd.PersistentFlags().StringVar(&cfgPath, "config", "data/seeds.yaml", "Path to config file")
	rootCmd.PersistentFlags().StringVar(&dbPath, "db", defaultDBPath(), "Path to SQLite database, or a Postgres DSN (postgres://...)")

	rootCmd.AddCommand(migrateCmd())
	rootCmd.AddCommand(ingestCmd())
	rootCmd.AddCommand(crawlCmd())
	rootCmd.AddCommand(redditCmd())
	rootCmd.AddCommand(xCmd())
	rootCmd.AddCommand(requeueStaleCmd())

	if err := rootCmd.Execute(); err != nil {
		log.Fatal(err)
	}
}

// defaultDBPath resolves the --db default: CIVIC_DATABASE_URL when set (so
// deploys can point the binary at Postgres via env, matching the
// CIVIC_DATABASE_URL name already used on the Python side), falling back to
// the SQLite path otherwise. An explicit --db flag still overrides either.
func defaultDBPath() string {
	if dsn := os.Getenv("CIVIC_DATABASE_URL"); dsn != "" {
		return dsn
	}
	return "data/civic_lens.db"
}

func migrateCmd() *cobra.Command {
	return &cobra.Command{
		Use:   "migrate",
		Short: "Apply database migrations",
		RunE: func(cmd *cobra.Command, args []string) error {
			a, err := app.NewDBOnly(cmd.Context(), dbPath)
			if err != nil {
				return err
			}
			defer a.Close()

			if err := a.Database.Migrate(cmd.Context()); err != nil {
				return err
			}

			fmt.Println("Migrations applied successfully")
			return nil
		},
	}
}

func ingestCmd() *cobra.Command {
	return &cobra.Command{
		Use:   "ingest",
		Short: "Ingest seeds from config (discover URLs)",
		RunE: func(cmd *cobra.Command, args []string) error {
			ctx := cmd.Context()

			a, err := app.New(ctx, app.Options{
				ConfigPath:     cfgPath,
				DBPath:         dbPath,
				RequireFetcher: true,
			})
			if err != nil {
				return err
			}
			defer a.Close()

			result, err := runner.NewIngestRunner(a).Run(ctx)
			if err != nil {
				return err
			}

			fmt.Printf("Total URLs added to frontier: %d\n", result.TotalDiscovered)
			return nil
		},
	}
}

func crawlCmd() *cobra.Command {
	var duration time.Duration

	cmd := &cobra.Command{
		Use:   "crawl",
		Short: "Run the crawl loop",
		RunE: func(cmd *cobra.Command, args []string) error {
			ctx, cancel := app.SignalContext(cmd.Context())
			defer cancel()

			a, err := app.New(ctx, app.Options{
				ConfigPath:      cfgPath,
				DBPath:          dbPath,
				RequireFetcher:  true,
				RequireRawStore: true,
				RequireRobots:   true,
			})
			if err != nil {
				return err
			}
			defer a.Close()

			result, err := runner.NewCrawlRunner(a, runner.CrawlOptions{
				Duration: duration,
			}).Run(ctx)
			if err != nil {
				return err
			}

			fmt.Printf("\n=== Crawl Summary ===\n")
			fmt.Printf("Fetched: %d\n", result.Fetched)
			fmt.Printf("Stored:  %d\n", result.Stored)
			fmt.Printf("Errors:  %d\n", result.Errors)

			q, i, d, f, _ := a.Frontier.Stats(context.Background())
			fmt.Printf("Frontier: queued=%d inflight=%d done=%d failed=%d\n", q, i, d, f)

			return nil
		},
	}

	cmd.Flags().DurationVar(&duration, "duration", 10*time.Minute, "How long to run the crawl")
	return cmd
}

func redditCmd() *cobra.Command {
	return &cobra.Command{
		Use:   "reddit",
		Short: "Fetch Reddit posts and comments",
		RunE: func(cmd *cobra.Command, args []string) error {
			ctx := cmd.Context()

			a, err := app.New(ctx, app.Options{
				ConfigPath:      cfgPath,
				DBPath:          dbPath,
				RequireRawStore: true,
			})
			if err != nil {
				return err
			}
			defer a.Close()

			result, err := runner.NewRedditRunner(a).Run(ctx)
			if err != nil {
				return err
			}

			fmt.Printf("Reddit ingestion complete: %d subreddits, %d posts\n",
				result.SubredditsProcessed, result.PostsIngested)
			return nil
		},
	}
}

func xCmd() *cobra.Command {
	return &cobra.Command{
		Use:   "x",
		Short: "Fetch X posts and comments",
		RunE: func(cmd *cobra.Command, args []string) error {
			ctx := cmd.Context()

			a, err := app.New(ctx, app.Options{
				ConfigPath:      cfgPath,
				DBPath:          dbPath,
				RequireRawStore: true,
			})
			if err != nil {
				return err
			}
			defer a.Close()

			result, err := runner.NewXRunner(a).Run(ctx)
			if err != nil {
				return err
			}

			fmt.Printf("X ingestion complete: %d queries, %d posts, %d users\n",
				result.QueriesProcessed, result.PostsIngested, result.UsersIngested)
			return nil
		},
	}
}

func requeueStaleCmd() *cobra.Command {
	var staleAge time.Duration

	cmd := &cobra.Command{
		Use:   "requeue-stale",
		Short: "Requeue stale in-flight items",
		RunE: func(cmd *cobra.Command, args []string) error {
			ctx := cmd.Context()

			a, err := app.New(ctx, app.Options{
				ConfigPath: cfgPath,
				DBPath:     dbPath,
			})
			if err != nil {
				return err
			}
			defer a.Close()

			result, err := runner.RunRequeueStale(ctx, a.Frontier, runner.RequeueOptions{
				StaleAge: staleAge,
			})
			if err != nil {
				return err
			}

			fmt.Printf("Requeued %d stale items\n", result.Recovered)
			return nil
		},
	}

	cmd.Flags().DurationVar(&staleAge, "stale", 10*time.Minute, "Age threshold for stale items")
	return cmd
}
