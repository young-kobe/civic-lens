#!/usr/bin/env bash
#
# Civic Lens dev orchestrator (Linux / WSL).
#
# Auto-creates analysis/.venv, loads .env, resolves Go/Node, and drives the
# Go ingestor + Python pipeline + API + UI. Bash port of the former run.ps1.
#
# Usage:  ./run.sh <command> [options]
#   ./run.sh help                     # list commands
#   ./run.sh analyze --tasks bot,text # run specific pipeline stages
#   ./run.sh crawl --duration 5m      # crawl for 5 minutes
#
set -euo pipefail

SCRIPT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DB="$SCRIPT_ROOT/data/civic_lens.db"
SEEDS="$SCRIPT_ROOT/data/seeds.yaml"
INGEST_BIN="$SCRIPT_ROOT/civic-ingest"
VENV="$SCRIPT_ROOT/analysis/.venv"
VENV_PY="$VENV/bin/python"
REQ="$SCRIPT_ROOT/analysis/requirements.txt"

# ----------------------------------------------------------------------------- #
#  Helpers                                                                      #
# ----------------------------------------------------------------------------- #

status() { printf '\033[36m[CivicLens]\033[0m %s\n' "$*"; }
die()    { printf '\033[31m[CivicLens] %s\033[0m\n' "$*" >&2; exit 1; }

_trim() {
    local s="$1"
    s="${s#"${s%%[![:space:]]*}"}"   # ltrim
    s="${s%"${s##*[![:space:]]}"}"   # rtrim
    printf '%s' "$s"
}

# Load .env line by line (split on the first '=', skip comments/blanks) so a
# value containing '=' survives. Existing environment variables win, so a shell
# export or systemd Environment= can override the file.
load_env() {
    [[ -f "$SCRIPT_ROOT/.env" ]] || return 0
    status "Loading .env..."
    local line key val
    while IFS= read -r line || [[ -n "$line" ]]; do
        line="$(_trim "$line")"
        [[ -z "$line" || "$line" == \#* ]] && continue
        [[ "$line" != *"="* ]] && continue
        key="$(_trim "${line%%=*}")"
        val="$(_trim "${line#*=}")"
        [[ -n "$key" && -z "${!key:-}" ]] && export "$key=$val"
    done < "$SCRIPT_ROOT/.env"
    # The && guard above returns 1 when the last line's var is already
    # exported; under set -e that would silently kill the script.
    return 0
}

find_go() {
    if command -v go >/dev/null 2>&1; then echo "go"; return; fi
    [[ -x /usr/local/go/bin/go ]] && { echo "/usr/local/go/bin/go"; return; }
    die "Go is not installed. Install Go 1.22+ from https://go.dev/dl/"
}

# ----------------------------------------------------------------------------- #
#  Go ingestor                                                                  #
# ----------------------------------------------------------------------------- #

build_ingest() {
    status "Building Go ingestion..."
    local go_bin; go_bin="$(find_go)"
    ( cd "$SCRIPT_ROOT/ingest" && "$go_bin" build -o "$INGEST_BIN" ./cmd/civic-ingest ) \
        || die "Go build failed"
    status "Built civic-ingest"
}

ensure_ingest() { [[ -x "$INGEST_BIN" ]] || build_ingest; }

run_migrate() { ensure_ingest; status "Running database migrations...";  "$INGEST_BIN" migrate --db "$DB"; }
run_ingest()  { ensure_ingest; status "Running seed ingestion...";       "$INGEST_BIN" ingest --config "$SEEDS" --db "$DB"; }
run_reddit()  { ensure_ingest; status "Fetching Reddit data...";         "$INGEST_BIN" reddit --config "$SEEDS" --db "$DB"; }
run_x()       { ensure_ingest; status "Fetching X (Twitter) data...";    "$INGEST_BIN" x --config "$SEEDS" --db "$DB"; }
run_crawl()   { ensure_ingest; status "Running crawl for ${DURATION}..."; "$INGEST_BIN" crawl --config "$SEEDS" --db "$DB" --duration "$DURATION"; }

# ----------------------------------------------------------------------------- #
#  Postgres (dev, pg-redesign Phase 1 groundwork)                              #
# ----------------------------------------------------------------------------- #

run_pg() {
    command -v docker >/dev/null 2>&1 || die "Docker is not installed."
    status "Starting Postgres (dev)..."
    docker compose up -d postgres || die "Failed to start postgres"
    status "Waiting for Postgres to become healthy..."
    local cid tries=0
    cid="$(docker compose ps -q postgres)"
    until [[ "$(docker inspect -f '{{.State.Health.Status}}' "$cid" 2>/dev/null)" == "healthy" ]]; do
        tries=$((tries + 1))
        [[ $tries -ge 30 ]] && die "Postgres did not become healthy in time (see: docker compose logs postgres)"
        sleep 2
    done
    status "Postgres is healthy at 127.0.0.1:5432."
}

# ----------------------------------------------------------------------------- #
#  Python (venv)                                                                #
# ----------------------------------------------------------------------------- #

ensure_venv() {
    if [[ ! -x "$VENV_PY" ]]; then
        status "Creating Python virtual environment..."
        command -v python3 >/dev/null 2>&1 || die "python3 is not installed (need 3.10+)."
        python3 -m venv "$VENV" || die "Failed to create virtualenv at $VENV"
    fi
}

pip_install() {
    [[ -f "$REQ" ]] && "$VENV_PY" -m pip install -r "$REQ" --quiet
}

run_api() {
    status "Starting Python API..."
    ensure_venv; pip_install
    PYTHONPATH="$SCRIPT_ROOT" "$VENV_PY" "$SCRIPT_ROOT/analysis/src/main.py"
}

run_analyze() {
    status "Running analysis pipeline..."
    ensure_venv; pip_install
    local args=(-m analysis.src.scheduler.job_runner)
    [[ -n "$TASKS" ]] && args+=(--tasks "$TASKS")
    [[ -n "$LIMIT" ]] && args+=(--limit "$LIMIT")
    if PYTHONPATH="$SCRIPT_ROOT" "$VENV_PY" "${args[@]}"; then
        status "Analysis pipeline complete. Cached snapshots saved to data/cache/"
    else
        die "Analysis pipeline failed"
    fi
}

run_analyze_pg() {
    status "Running Postgres-stack analysis pipeline..."
    ensure_venv; pip_install
    local args=(-m analysis.src.scheduler.pipeline)
    [[ -n "$TASKS" ]] && args+=(--tasks "$TASKS")
    [[ -n "$LIMIT" ]] && args+=(--limit "$LIMIT")
    if PYTHONPATH="$SCRIPT_ROOT" "$VENV_PY" "${args[@]}"; then
        status "Analysis pipeline complete."
    else
        die "Analysis pipeline failed"
    fi
}

run_refresh_accounts() {
    status "Refreshing known_political_x_accounts.yaml from UCSD libguide..."
    ensure_venv; pip_install
    local args=(-m analysis.src.etl.refresh_accounts)
    [[ -n "$DRY_RUN" ]] && args+=(--dry-run)
    PYTHONPATH="$SCRIPT_ROOT" "$VENV_PY" "${args[@]}" || die "refresh-accounts failed"
    status "Done. Review the diff summary above; commit the YAML if it looks right."
}

# ----------------------------------------------------------------------------- #
#  UI + combined dev                                                            #
# ----------------------------------------------------------------------------- #

ensure_ui_deps() {
    command -v npm >/dev/null 2>&1 || die "npm is not installed. Install Node.js 18+."
    if [[ ! -d "$SCRIPT_ROOT/ui/node_modules" ]]; then
        status "Installing UI dependencies..."
        ( cd "$SCRIPT_ROOT/ui" && npm install )
    fi
}

run_ui() {
    status "Starting React frontend..."
    ensure_ui_deps
    ( cd "$SCRIPT_ROOT/ui" && npm run dev )
}

run_dev() {
    status "Launching dev environment (API + UI). Ctrl-C stops both."
    ensure_venv; pip_install; ensure_ui_deps
    # Kill the whole process group on exit so Ctrl-C tears down both servers.
    trap 'trap - INT TERM EXIT; kill 0' INT TERM EXIT
    ( PYTHONPATH="$SCRIPT_ROOT" "$VENV_PY" "$SCRIPT_ROOT/analysis/src/main.py" ) &
    ( cd "$SCRIPT_ROOT/ui" && npm run dev ) &
    wait
}

usage() {
    cat <<'EOF'
Usage: ./run.sh <command> [options]

Commands:
  build             Build the Go ingestion binary (civic-ingest)
  migrate           Apply database migrations
  ingest            Discover URLs from seed feeds
  crawl             Run the web crawler          [--duration <dur>, default 10m]
  reddit            Fetch Reddit posts/comments
  x                 Fetch X/Twitter posts
  pg                Start Postgres (dev; pg-redesign Phase 1 groundwork, not yet live)
  analyze           Run analysis pipeline (ETL + AI + caching)
                                                 [--tasks <list>] [--limit <n>]
  analyze-pg        Run the Postgres-stack pipeline (scheduler/pipeline.py)
                                                 [--tasks <list>] [--limit <n>]
  refresh-accounts  Refresh known_political_x_accounts.yaml   [--dry-run]
  api               Start the FastAPI server (serves cached data) on :8000
  ui                Start the React frontend (Vite) on :5173
  dev               Start API + UI together (Ctrl-C stops both)
  all               migrate -> ingest -> crawl 5m -> analyze -> dev
  help              Show this message

Typical workflow:
  ./run.sh crawl                # fetch news articles
  ./run.sh analyze              # run the AI analysis pipeline
  ./run.sh dev                  # start API + UI
EOF
}

# ----------------------------------------------------------------------------- #
#  Arg parsing + dispatch                                                       #
# ----------------------------------------------------------------------------- #

COMMAND="${1:-help}"
shift || true

TASKS=""
LIMIT=""
DRY_RUN=""
DURATION="10m"

while [[ $# -gt 0 ]]; do
    case "$1" in
        -t|--tasks)    TASKS="${2:-}";    shift 2 ;;
        -l|--limit)    LIMIT="${2:-}";    shift 2 ;;
        -d|--duration) DURATION="${2:-}"; shift 2 ;;
        --dry-run)     DRY_RUN=1;         shift ;;
        *) die "Unknown option: $1 (see ./run.sh help)" ;;
    esac
done

load_env

case "$COMMAND" in
    build)            build_ingest ;;
    migrate)          run_migrate ;;
    ingest)           run_migrate; run_ingest ;;
    crawl)            run_migrate; run_crawl ;;
    reddit)           run_migrate; run_reddit ;;
    x)                run_migrate; run_x ;;
    pg)               run_pg ;;
    api)              run_api ;;
    analyze)          run_analyze ;;
    analyze-pg)       run_analyze_pg ;;
    refresh-accounts) run_refresh_accounts ;;
    ui)               run_ui ;;
    dev)              run_dev ;;
    all)
        run_migrate
        run_ingest
        DURATION="5m"
        run_crawl
        run_analyze
        run_dev
        ;;
    help|-h|--help)   usage ;;
    *) die "Unknown command: $COMMAND (see ./run.sh help)" ;;
esac
