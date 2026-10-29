#!/usr/bin/env bash
# Run after each update.sh. It prints one PASS or FAIL line per check and exits 1 if a check failed.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

site=https://civic-lens.info
api=https://api.civic-lens.info
user_agent="CivicLens/0.1 (civic-lens.info)"
caddyfile="$deploy_directory/caddy/Caddyfile"
search_env="$CIVIC_LENS_ROOT/env/search.env"
backup_stamp="$CIVIC_LENS_ROOT/backup/last-success"
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
  if [[ ! -r $caddyfile ]]; then
    fail "$name: cannot read $caddyfile. Run the script from the checkout with sudo."
    return
  fi
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
  elif ! grep -qi '<html' "$body_file"; then
    fail "Missing page: expected the release 404 page (HTML), got a plain body. Check that the active release has 404.html."
  elif [[ $cache != no-store ]]; then
    fail "Missing page: expected Cache-Control: no-store, got '${cache:-no header}'. Check the handle_errors block of the Caddyfile."
  else
    pass "Missing page: status 404, release 404 page, Cache-Control no-store"
  fi
}

check_internal_route() {
  local status
  status=$(status_of "$(fetch "$api/internal/v1/reports")")
  if [[ $status == 404 ]]; then
    pass "Internal route: $api/internal/* returns 404"
  else
    fail "Internal route: expected status 404 from $api/internal/v1/reports, got '${status:-no response}'. Check the handle /internal/* block of the Caddyfile."
  fi
}

# Cloudflare sets CF-Connecting-IP to the real address of the caller, so the check looks for that address in the log.
check_no_access_log() {
  local query_value="probe$RANDOM$RANDOM$RANDOM" address log
  address=$(curl --silent --max-time 15 --user-agent "$user_agent" "$site/cdn-cgi/trace" | sed -n 's/^ip=//p')
  if [[ -z $address ]]; then
    fail "Access log: could not read your address from $site/cdn-cgi/trace. Check that Cloudflare serves the site."
    return
  fi
  fetch "$site/no-such-page?$query_value=$query_value" > /dev/null
  if ! log=$(compose logs --no-color --since 5m caddy 2>&1); then
    fail "Access log: could not read the Caddy log. Check that Docker runs and that you used sudo."
  elif grep -qF -e "$query_value" -e "$address" <<< "$log"; then
    fail "Access log: expected no trace of the probe request, but the Caddy log holds '$query_value' or your address '$address'. Check for a log directive in a Caddyfile site block."
  else
    pass "Access log: the Caddy log holds neither the probe query nor your address"
  fi
}

check_backup_age() {
  local stamp stamp_epoch age_hours
  if [[ ! -r $backup_stamp ]]; then
    fail "Backup: $backup_stamp is missing, so no backup has succeeded. Run one: sudo systemctl start civic-lens-backup.service. Then read: sudo journalctl -u civic-lens-backup.service"
    return
  fi
  stamp=$(< "$backup_stamp")
  if ! stamp_epoch=$(date --date "$stamp" +%s); then
    fail "Backup: could not read the time '$stamp' in $backup_stamp. The next good backup writes it again."
  elif age_hours=$((($(date +%s) - stamp_epoch) / 3600)); ((age_hours >= backup_max_age_hours)); then
    fail "Backup: expected a good backup in the last $backup_max_age_hours hours, the last one ended $age_hours hours ago ($stamp). Read: sudo journalctl -u civic-lens-backup.service and systemctl list-timers civic-lens-backup.timer"
  else
    pass "Backup: the last good backup ended $stamp"
  fi
}

# Cloudflare refuses a request that already carries CF-Connecting-IP (403, error code 1000), so a reader cannot choose the address that the limit counts.
check_forged_header() {
  local status
  status=$(status_of "$(fetch --header "CF-Connecting-IP: 198.18.0.1" "$api/v1/search?q=check")")
  if [[ $status == 403 ]]; then
    pass "Forged header: Cloudflare refuses a request with a forged CF-Connecting-IP (403)"
  else
    fail "Forged header: expected status 403 from Cloudflare for a forged CF-Connecting-IP, got '${status:-no response}'. Check that $api goes through Cloudflare and not straight to the machine."
  fi
}

# The limit uses fixed one-minute windows. Twice the limit plus two requests, sent 20 at a time, puts more than the limit in one window.
check_rate_limit() {
  local limit ranges attempts statuses
  if [[ ! -r $search_env ]]; then
    fail "Rate limit: cannot read $search_env. Run the script with sudo, and check that install.sh created the file."
    return
  fi
  limit=$(sed -n 's/^CIVIC_LENS_REQUESTS_PER_MINUTE=//p' "$search_env" | tail -n 1 | tr -d "\"'")
  limit=${limit:-60}
  ranges=$(sed -n 's/^CIVIC_LENS_TRUSTED_PROXY_RANGES=//p' "$search_env" | tail -n 1 | tr -d "\"'")
  if [[ ! $limit =~ ^[0-9]+$ ]]; then
    fail "Rate limit: CIVIC_LENS_REQUESTS_PER_MINUTE in $search_env is '$limit', not a whole number. Fix the file."
    return
  fi
  if [[ -z $ranges ]]; then
    fail "Rate limit: CIVIC_LENS_TRUSTED_PROXY_RANGES in $search_env is empty, so all readers share one limit. Set it as in deploy/env/search.env.example."
    return
  fi
  attempts=$((2 * (limit + 1)))
  echo "NOTE  The next check sends $attempts requests. It blocks your own address on the search service for up to one minute."
  statuses=$(seq 1 "$attempts" |
    xargs -P 20 -I {} curl --silent --max-time 15 --user-agent "$user_agent" --output /dev/null --write-out '%{http_code}\n' \
      "$api/v1/search?q=check&probe={}" || true)
  if grep -qx 429 <<< "$statuses"; then
    pass "Rate limit: the search service answered 429 within $attempts requests"
  else
    fail "Rate limit: expected status 429 within $attempts requests, got none. Check the limit in $search_env and that the search service runs."
  fi
}

check_noindex "Noindex site" "$site/"
check_noindex "Noindex api" "$api/v1/release"
check_missing_page
check_internal_route
check_no_access_log
check_backup_age
check_forged_header
check_rate_limit

if (( failures > 0 )); then
  echo "$failures check(s) failed."
  exit 1
fi
echo "All checks passed."
