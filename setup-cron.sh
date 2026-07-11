#!/usr/bin/env bash
#
# Civic Lens analysis pipeline - cron scheduling (Linux / WSL dev).
#
# Installs (or removes) a crontab entry that runs `./run.sh analyze` on a fixed
# interval. Linux port of the former setup-scheduled-task.ps1 (Windows Task
# Scheduler). For PRODUCTION scheduling use the systemd timer from install.sh
# instead — cron here is a dev convenience.
#
# Usage:
#   ./setup-cron.sh                 # install, every 6 hours (4 runs/day)
#   ./setup-cron.sh --runs-per-day 8
#   ./setup-cron.sh --remove
#
# Note: WSL does not run cron unless it is started (`sudo service cron start`,
# or enable systemd in /etc/wsl.conf). This script only manages the crontab line.
#
set -euo pipefail

SCRIPT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUN_SH="$SCRIPT_ROOT/run.sh"
TAG="# civic-lens-analyze"     # marker so we can find/replace our own line

RUNS_PER_DAY=4
REMOVE=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        -n|--runs-per-day) RUNS_PER_DAY="${2:-}"; shift 2 ;;
        --remove)          REMOVE=1; shift ;;
        -h|--help)
            grep '^#' "$0" | sed 's/^# \{0,1\}//'
            exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done

# Current crontab minus any prior civic-lens line (empty if no crontab yet).
current="$(crontab -l 2>/dev/null | grep -vF "$TAG" || true)"

if [[ "$REMOVE" == "1" ]]; then
    if [[ -n "$current" ]]; then
        printf '%s\n' "$current" | crontab -
    else
        crontab -r 2>/dev/null || true
    fi
    echo "Removed the civic-lens analyze cron entry."
    exit 0
fi

[[ "$RUNS_PER_DAY" =~ ^[0-9]+$ && "$RUNS_PER_DAY" -ge 1 ]] \
    || { echo "runs-per-day must be a positive integer" >&2; exit 1; }

interval=$(( 24 / RUNS_PER_DAY ))
(( interval < 1 )) && interval=1

# Run at minute 0 every $interval hours. Redirect output to a log so cron mail
# does not pile up; run.sh loads .env itself.
line="0 */$interval * * * cd $SCRIPT_ROOT && $RUN_SH analyze >> $SCRIPT_ROOT/data/analyze-cron.log 2>&1 $TAG"

printf '%s\n' "$current" "$line" | grep -v '^$' | crontab -

echo "Installed cron entry: analyze every $interval hour(s) ($RUNS_PER_DAY runs/day)."
echo "  View:   crontab -l"
echo "  Logs:   $SCRIPT_ROOT/data/analyze-cron.log"
echo "  Remove: ./setup-cron.sh --remove"
echo "  (WSL only: ensure cron is running - 'sudo service cron start')"
