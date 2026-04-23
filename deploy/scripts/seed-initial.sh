#!/usr/bin/env bash
# One-shot initial seed for a fresh prod install. Runs the Go ingest + X
# fetch + analysis pipeline end-to-end so the UI has enough content on
# first load to populate the 24h / 7d windows.
#
# Idempotent: can be re-run without harm — ingest de-dupes by content hash,
# the ETL loader skips already-processed docs, and snapshots overwrite.
#
# Why this exists: on a fresh prod box the aggregator windows are empty.
# RSS feeds typically carry 20-50 most-recent articles (covers days), and
# the X API's /2/tweets/search/recent returns the past 7 days. Running the
# fetchers once and feeding a capped slice into the analyzer primes the
# UI without burning a day's LLM budget on the first fire.
#
# COST ENVELOPE (Gemini Flash, at ~$0.0005/call):
#   SEED_LIMIT=50 docs × 4 LLM stages (bot + text + propaganda + claims)
#       ≈ 200 calls ≈ $0.10 per seed run.
#   Override via environment: SEED_LIMIT=100 bash seed-initial.sh etc.
#   The normal timer-driven analyze run has no limit and burns through
#   the backlog at its configured batch size (CIVIC_LOADER_BATCH_SIZE).
#
# Usage (on prod, as root or via sudo):
#   bash /opt/civic-lens/deploy/scripts/seed-initial.sh
#   SEED_LIMIT=100 bash /opt/civic-lens/deploy/scripts/seed-initial.sh
#
# Runtime: ~20-30 min on a warm box.

set -euo pipefail

REPO=/opt/civic-lens
DB=${CIVIC_DB_PATH:-/var/lib/civic-lens/data/civic_lens.db}
SEEDS=$REPO/data/seeds.yaml
# Per-stage doc cap. At 50 and $0.0005/call the seed costs ~$0.10
# regardless of how many articles got crawled. The first post-seed timer
# fire picks up whatever didn't make the cut.
SEED_LIMIT=${SEED_LIMIT:-50}
# Shorter crawl than the 30-min you'd use for an exhaustive first pass.
# At 15m the crawler drains most RSS feeds once; anything further can
# wait for the hourly steady-state timer to take care of.
SEED_CRAWL_DURATION=${SEED_CRAWL_DURATION:-15m}

if [[ ! -x "$REPO/civic-ingest" ]]; then
    echo "seed-initial: $REPO/civic-ingest missing — run deploy.sh first" >&2
    exit 1
fi

echo "[1/4] migrate"
"$REPO/civic-ingest" migrate --db "$DB"

echo "[2/4] RSS ingest (enqueue frontier) + crawl ($SEED_CRAWL_DURATION)"
"$REPO/civic-ingest" ingest \
    --config "$SEEDS" \
    --db "$DB"
"$REPO/civic-ingest" crawl \
    --config "$SEEDS" \
    --db "$DB" \
    --duration "$SEED_CRAWL_DURATION"

echo "[3/4] X fetch (7 days of tracked handles + queries)"
# /2/tweets/search/recent returns the past 7 days; a single invocation of
# `civic-ingest x` runs every configured query once.
"$REPO/civic-ingest" x \
    --config "$SEEDS" \
    --db "$DB"

echo "[4/4] analysis pipeline with --limit $SEED_LIMIT (cost cap)"
# --limit bounds per-stage doc counts → predictable cost envelope.
# No --budget-seconds: the seed is small enough that wall-clock is never
# the constraint. Subsequent timer-driven runs inherit CIVIC_BUDGET_SECONDS
# from civic-lens-analyze.service.
cd "$REPO"
PYTHONPATH="$REPO" "$REPO/.venv/bin/python" -m analysis.src.scheduler.job_runner \
    --limit "$SEED_LIMIT"

echo
echo "seed-initial complete. Bounded by SEED_LIMIT=$SEED_LIMIT."
echo "  UI cache now populated; first timer fire drains the backlog."
echo "  Verify: curl -sS http://127.0.0.1:8000/api/snapshot-status | jq"
