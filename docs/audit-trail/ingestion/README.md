# Ingestion audit trail

Changes to the Go crawler, RSS / Reddit / X fetchers, frontier state machine, and raw storage layout.

Scope: `ingest/` + `data/seeds.yaml` + `data/migrations/` entries that affect the crawler's view of the world.

Out of scope: aggregation, LLM analysis, API shape — those live in `../analysis/`, `../api/`.
