#!/usr/bin/env bash
# Emails once when the worker is unhealthy or the data disk is low. A marker file stops repeat email until the problem clears.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

min_free_gb=10
state_directory=${CIVIC_LENS_HEALTH_STATE:-/run/civic-lens}

report() {
  local name=$1 failed=$2 subject=$3 text=$4
  local marker="$state_directory/$name.alerted"
  if [[ $failed != yes ]]; then
    rm -f "$marker"
    return
  fi
  echo "$subject" >&2
  if [[ -e $marker ]]; then
    return
  fi
  "$(dirname "${BASH_SOURCE[0]}")/send-alert.sh" "$subject" "$text Host $(hostname), time $(date --utc +%Y-%m-%dT%H:%M:%SZ)."
  touch "$marker"
}

mkdir -p "$state_directory"

worker_failed=no
compose run --rm -T cli worker health > /dev/null || worker_failed=yes
report worker "$worker_failed" "Civic Lens: worker unhealthy" \
  "The command worker health failed. Run civic-lens-ops worker health to read its line."

free_gb=$(($(df --output=avail --block-size=1 "$CIVIC_LENS_ROOT" | tail -n 1) / 1024 ** 3))
disk_failed=no
if ((free_gb < min_free_gb)); then
  disk_failed=yes
fi
report disk "$disk_failed" "Civic Lens: disk space low" \
  "Free space under $CIVIC_LENS_ROOT is $free_gb GB. The limit is $min_free_gb GB."
