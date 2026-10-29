#!/usr/bin/env bash
# Run after each update.sh. It prints one PASS or FAIL line per check and exits 1 if a check failed.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

site=https://civic-lens.info
api=https://api.civic-lens.info
user_agent="CivicLens/0.1 (civic-lens.info)"
caddyfile="$deploy_directory/caddy/Caddyfile"
search_env="$CIVIC_LENS_ROOT/env/search.env"
backup_unit=civic-lens-backup.service
backup_max_age_hours=26
failures=0

body_file=$(mktemp)
trap 'rm -f "$body_file"' EXIT

pass() { echo "PASS  $1"; }
fail() { echo "FAIL  $1"; failures=$((failures + 1)); }

# Prints the status line and headers of one response. The body goes to $body_file.
fetch() {
  curl --silent --max-time 15 --user-agent "$user_agent" --output "$body_file" --dump-header - "$@" | tr -d '\r' || true
}

status_of() { awk 'NR == 1 { print $2 }' <<< "$1"; }

header_of() { { grep -i "^$2:" <<< "$1" || true; } | head -n 1 | cut -d: -f2- | sed 's/^ *//'; }

noindex_expected() { grep -Eq '^[[:space:]]*import noindex[[:space:]]*$' "$caddyfile"; }

check_noindex() {
  local name=$1 url=$2 headers value
  headers=$(fetch "$url")
  if [[ -z $headers ]]; then
    fail "$name: no response from $url. Check that the tunnel and Caddy run: sudo deploy/scripts/compose.sh ps"
    return
  fi
  value=$(header_of "$headers" x-robots-tag)
  if noindex_expected && [[ $value != noindex ]]; then
    fail "$name: expected X-Robots-Tag: noindex because the Caddyfile has 'import noindex', got '${value:-no header}' from $url. Check that Caddy was recreated by update.sh."
  elif ! noindex_expected && [[ -n $value ]]; then
    fail "$name: expected no X-Robots-Tag because the Caddyfile has no 'import noindex', got '$value' from $url. Check for a Cloudflare rule that adds it, or a Caddy that was not recreated."
  else
    pass "$name: X-Robots-Tag is ${value:-absent}, as the Caddyfile says"
  fi
}

check_missing_page() {
  local headers cache
  headers=$(fetch "$site/no-such-page-$RANDOM")
  cache=$(header_of "$headers" cache-control)
  if [[ $(status_of "$headers") != 404 ]]; then
    fail "Missing page: expected status 404 from $site, got '$(status_of "$headers")'. Check that the active release has files: sudo deploy/scripts/compose.sh run --rm cli releases list"
  elif [[ $cache != no-store ]]; then
    fail "Missing page: expected Cache-Control: no-store, got '${cache:-no header}'. Check the handle_errors block of the Caddyfile."
  elif ! grep -qi '<html' "$body_file"; then
    fail "Missing page: expected the release 404 page (HTML), got a plain body. Check that the active release has 404.html."
  else
    pass "Missing page: status 404, release 404 page, Cache-Control no-store"
  fi
}

check_internal_route() {
  local status
  status=$(status_of "$(fetch "$api/internal/check-live")")
  if [[ $status == 404 ]]; then
    pass "Internal route: $api/internal/* returns 404"
  else
    fail "Internal route: expected status 404 from $api/internal/check-live, got '${status:-no response}'. Check the handle /internal/* block of the Caddyfile."
  fi
}

check_no_access_log() {
  local query_value="probe$RANDOM$RANDOM$RANDOM" address="203.0.113.$((RANDOM % 254 + 1))" log
  fetch --header "CF-Connecting-IP: $address" "$site/no-such-page?$query_value=$query_value" > /dev/null
  if ! log=$(compose logs --no-color --since 5m caddy 2>&1); then
    fail "Access log: could not read the Caddy log. Check that Docker runs and that you used sudo."
  elif grep -qF -e "$query_value" -e "$address" <<< "$log"; then
    fail "Access log: expected no trace of the probe request, but the Caddy log holds '$query_value' or '$address'. Check for a log directive in a Caddyfile site block."
  else
    pass "Access log: the Caddy log holds neither the probe query nor the probe address"
  fi
}

check_backup_age() {
  local result exit_time exit_epoch age_hours
  result=$(systemctl show "$backup_unit" --property=Result --value)
  exit_time=$(systemctl show "$backup_unit" --property=ExecMainExitTimestamp --value)
  if [[ -z $exit_time ]]; then
    fail "Backup: $backup_unit never ran. Run it: sudo systemctl start $backup_unit. Then check that the timer is enabled: systemctl list-timers civic-lens-backup.timer"
  elif [[ $result != success ]]; then
    fail "Backup: expected the last result to be success, got '$result'. Read the log: sudo journalctl -u $backup_unit"
  elif ! exit_epoch=$(date --date "$exit_time" +%s); then
    fail "Backup: could not read the last run time '$exit_time'. Check: systemctl show $backup_unit"
  elif age_hours=$((($(date +%s) - exit_epoch) / 3600)); ((age_hours >= backup_max_age_hours)); then
    fail "Backup: expected a run in the last $backup_max_age_hours hours, the last run ended $age_hours hours ago ($exit_time). Check the timer: systemctl list-timers civic-lens-backup.timer"
  else
    pass "Backup: the last run succeeded and ended $exit_time"
  fi
}

# Cloudflare replaces CF-Connecting-IP, so every request below counts for the real caller. If the header were trusted from a reader, each forged value would get its own count and none would reach the limit.
# The limit uses fixed one-minute windows. Twice the limit plus two requests puts more than the limit in one window, even when a window ends during the loop.
check_forged_header_limit() {
  local limit=60 attempt attempts status=""
  if [[ -r $search_env ]]; then
    limit=$(sed -n 's/^CIVIC_LENS_REQUESTS_PER_MINUTE=//p' "$search_env" | tail -n 1)
    limit=${limit:-60}
  fi
  if [[ ! $limit =~ ^[0-9]+$ ]]; then
    fail "Rate limit: CIVIC_LENS_REQUESTS_PER_MINUTE in $search_env is '$limit', not a whole number. Fix the file."
    return
  fi
  attempts=$((2 * (limit + 1)))
  echo "NOTE  The next check sends up to $attempts requests. It blocks your own address on the search service for up to one minute."
  for ((attempt = 1; attempt <= attempts; attempt++)); do
    status=$(status_of "$(fetch --header "CF-Connecting-IP: 198.18.$((attempt / 250)).$((attempt % 250 + 1))" "$api/v1/search?q=check")")
    if [[ $status == 429 ]]; then break; fi
  done
  if [[ $status == 429 ]]; then
    pass "Rate limit: request $attempt with forged CF-Connecting-IP values got 429, so the header cannot be forged"
  else
    fail "Rate limit: expected status 429 within $attempts requests with a different forged CF-Connecting-IP each time, the last got '${status:-no response}'. Check that requests reach the service through Cloudflare, and the limit in $search_env."
  fi
}

check_noindex "Noindex site" "$site/"
check_noindex "Noindex api" "$api/v1/release"
check_missing_page
check_internal_route
check_no_access_log
check_backup_age
check_forged_header_limit

if (( failures > 0 )); then
  echo "$failures check(s) failed."
  exit 1
fi
echo "All checks passed."
