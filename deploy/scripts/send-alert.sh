#!/usr/bin/env bash
# Sends one alert email through Resend. It adds the host name and the time to the text.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

subject=${1:?Usage: send-alert.sh <subject> <text>}
text=${2:?Usage: send-alert.sh <subject> <text>}
env_file="$CIVIC_LENS_ROOT/env/alert.secret.env"

if [[ ! -r $env_file ]]; then
  echo "Cannot read $env_file. No alert was sent." >&2
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

if [[ $subject$text == *[\"\\]* ]]; then
  echo "The subject or text has a quote or a backslash." >&2
  exit 1
fi
text="$text Host $(hostname), time $(date --utc +%Y-%m-%dT%H:%M:%SZ)."

body="{\"from\":\"alerts@civic-lens.info\",\"to\":[\"$ALERT_TO\"],\"subject\":\"$subject\",\"text\":\"$text\"}"

curl --fail --silent --show-error --max-time 30 \
  --header "Content-Type: application/json" --header "@$header_file" \
  --data "$body" https://api.resend.com/emails > /dev/null
