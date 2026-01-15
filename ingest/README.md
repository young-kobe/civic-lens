# Civic Lens - Ingestion Module

This module is responsible for crawling and ingesting content from RSS feeds and Reddit.

## Architecture
-   **Language**: Go (Golang)
-   **Storage**: SQLite (`data/civic_lens.db`)
-   **Components**:
    -   `crawler`: Resumable, polite web crawler.
    -   `frontier`: Manages URL queues and priorities.
    -   `parser`: Normalizes HTML content.

## Workflows

### 1. Build
Build the CLI tool:
```bash
go build -o civic-ingest ./cmd/civic-ingest
```

### 2. Run Crawl
To start the crawler:
```bash
./civic-ingest crawl --seeds data/seeds.yaml
```

### 3. Database
The ingestor writes to `data/schema.sql` tables:
-   `pages`: Crawl frontier state.
-   `articles_raw`: Raw HTML content.
-   `reddit_posts_raw`: Raw JSON content.

## Configuration
Configuration is handled via command line flags and the seeds file.
