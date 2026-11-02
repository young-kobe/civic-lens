#!/usr/bin/env bash
# Emails once when the disk is low or the worker is unhealthy. A marker file stops repeat email until the problem clears.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

min_free_gb=10
state_directory=/run/civic-lens
send_failed=no

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
  if "$(dirname "${BASH_SOURCE[0]}")/send-alert.sh" "$subject" "$text"; then
    touch "$marker"
  else
    send_failed=yes
  fi
}

free_gb=$(($(df --output=avail --block-size=1 "$CIVIC_LENS_ROOT" | tail -n 1) / 1024 ** 3))
disk_failed=no
if ((free_gb < min_free_gb)); then
  disk_failed=yes
fi
report disk "$disk_failed" "Civic Lens: disk space low" \
  "Free space under $CIVIC_LENS_ROOT is $free_gb GB. The limit is $min_free_gb GB."

worker_failed=no
timeout 120 "$(dirname "${BASH_SOURCE[0]}")/compose.sh" run --rm --no-deps -T cli worker health || worker_failed=yes
report worker "$worker_failed" "Civic Lens: worker unhealthy" \
  "The command worker health failed. Run civic-lens-ops worker health to read its line."

if [[ $send_failed == yes ]]; then
  exit 1
fi
