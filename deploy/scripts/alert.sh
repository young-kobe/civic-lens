#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

unit=${1:?Usage: alert.sh <failed unit name>}
env_file="$CIVIC_LENS_ROOT/env/alert.secret.env"

if [[ ! $unit =~ ^[A-Za-z0-9@:._-]+$ ]]; then
  echo "The unit name has characters that are not allowed." >&2
  exit 1
fi
if [[ ! -r $env_file ]]; then
  echo "Cannot read $env_file. No alert was sent for $unit." >&2
  exit 1
fi
# shellcheck source=/dev/null
source "$env_file"
if [[ ! ${ALERT_TO:?} =~ ^[^\"\\[:space:]]+$ ]]; then
  echo "ALERT_TO in $env_file is not a plain address." >&2
  exit 1
fi

umask 077
header_file=$(mktemp)
trap 'rm -f "$header_file"' EXIT
echo "Authorization: Bearer ${RESEND_API_KEY:?}" > "$header_file"

host=$(hostname)
time=$(date --utc +%Y-%m-%dT%H:%M:%SZ)
text="The unit $unit failed on $host at $time. Read the log with: journalctl -u $unit"
body="{\"from\":\"alerts@civic-lens.info\",\"to\":[\"$ALERT_TO\"],\"subject\":\"Civic Lens: $unit failed\",\"text\":\"$text\"}"

if ! curl --fail --silent --show-error --max-time 30 \
  --header "Content-Type: application/json" --header "@$header_file" \
  --data "$body" https://api.resend.com/emails > /dev/null; then
  echo "The alert email for $unit was not sent." >&2
  exit 1
fi
echo "Sent the alert email for $unit."
