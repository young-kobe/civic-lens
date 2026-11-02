#!/usr/bin/env bash
set -euo pipefail
script_directory=$(dirname "${BASH_SOURCE[0]}")

unit=${1:?Usage: alert.sh <failed unit name>}

if [[ ! $unit =~ ^[A-Za-z0-9@:._-]+$ ]]; then
  echo "The unit name has characters that are not allowed." >&2
  exit 1
fi

host=$(hostname)
time=$(date --utc +%Y-%m-%dT%H:%M:%SZ)
text="The unit $unit failed on $host at $time. Read the log with: journalctl -u $unit"

if ! "$script_directory/send-alert.sh" "Civic Lens: $unit failed" "$text"; then
  echo "The alert email for $unit was not sent." >&2
  exit 1
fi
echo "Sent the alert email for $unit."
