# Go Migration Walkthrough

Replaced the C++ ingestion component with a Go implementation.

## New Directory Structure

```
ingest/
├── go.mod
├── cmd/civic-ingest/main.go    # CLI with all commands
└── internal/
    ├── config/config.go         # YAML config loader
    ├── model/model.go           # Shared types
    ├── frontier/                 # State machine
    │   ├── frontier.go
    │   └── frontier_test.go
    ├── httpclient/client.go     # Rate-limited fetcher
    ├── robots/robots.go         # robots.txt checker
    ├── extract/
    │   ├── rss/rss.go           # RSS/Atom parser
    │   ├── html/html.go         # HTML metadata extractor
    │   └── reddit/reddit.go     # Reddit API client
    ├── storage/
    │   ├── db/
    │   │   ├── db.go            # SQLite wrapper
    │   │   └── migrations/001_initial.sql
    │   └── rawstore/rawstore.go # Content-addressed storage
    └── util/
        ├── url.go               # URL canonicalization
        └── url_test.go
```

## CLI Commands

| Command | Description |
|---------|-------------|
| `civic-ingest migrate` | Apply DB migrations |
| `civic-ingest ingest` | Discover URLs from RSS seeds |
| `civic-ingest crawl --duration 10m` | Run crawl loop |
| `civic-ingest reddit` | Fetch Reddit posts/comments |
| `civic-ingest requeue-stale` | Recover stuck items |

## Updated run.ps1

| Command | Description |
|---------|-------------|
| `.\run.ps1 build` | Build Go binary |
| `.\run.ps1 ingest` | Discover URLs |
| `.\run.ps1 crawl` | Run crawler |
| `.\run.ps1 app` | Start Streamlit |
| `.\run.ps1 all` | Full pipeline |

## Next Steps

> [!IMPORTANT]
> Go 1.22+ must be installed: https://go.dev/dl/

After installing Go:
```powershell
cd ingest
go mod tidy
go test ./...
```

Then run the ingestion:
```powershell
.\run.ps1 ingest
.\run.ps1 crawl
```
