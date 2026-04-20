#!/usr/bin/env bash
# Pin ingress on :80 and :443 to Cloudflare's published IP ranges. Without
# this, anyone who discovers the VPS IP bypasses both Cloudflare Access
# and the Authenticated Origin Pulls gate (audit §1.16).
#
# Idempotent: deletes prior civic-lens-owned rules before re-creating them
# so a range rotation at Cloudflare doesn't accumulate stale ALLOW entries.
# Safe to re-run. Called monthly by civic-lens-firewall-refresh.timer.
#
# Must run as root.
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
    echo "firewall.sh must run as root" >&2
    exit 1
fi

CF_V4_URL=https://www.cloudflare.com/ips-v4
CF_V6_URL=https://www.cloudflare.com/ips-v6
TMP=$(mktemp -d)
trap "rm -rf $TMP" EXIT

curl -fsS "$CF_V4_URL" -o "$TMP/cf-v4.txt"
curl -fsS "$CF_V6_URL" -o "$TMP/cf-v6.txt"

# Sanity — if Cloudflare ever returns an empty list, bail rather than nuke
# the ruleset and lock the admin out.
if [[ ! -s "$TMP/cf-v4.txt" ]]; then
    echo "refused to proceed: Cloudflare IPv4 list is empty" >&2
    exit 1
fi

# Strip all existing civic-lens-CF ALLOW rules (identified by the comment).
# `ufw status numbered` emits them so we can delete by number in reverse
# order, avoiding renumbering issues.
# shellcheck disable=SC2016
ufw status numbered \
    | awk -F'[][]' '/# civic-lens-cf/ {print $2}' \
    | sort -rn \
    | while read -r n; do
        ufw --force delete "$n" >/dev/null
    done

# Default posture — deny incoming, allow outgoing, explicit SSH + CF.
ufw default deny incoming >/dev/null
ufw default allow outgoing >/dev/null

# SSH always open. Port-change is out of scope; fail2ban + MaxAuthTries do
# the heavy lifting.
ufw allow 22/tcp comment 'civic-lens-ssh' >/dev/null

add_cf() {
    local ip=$1
    ufw allow proto tcp from "$ip" to any port 80,443 comment 'civic-lens-cf' >/dev/null
}

while IFS= read -r ip; do
    [[ -z "$ip" ]] && continue
    add_cf "$ip"
done < "$TMP/cf-v4.txt"

while IFS= read -r ip; do
    [[ -z "$ip" ]] && continue
    add_cf "$ip"
done < "$TMP/cf-v6.txt"

ufw --force enable >/dev/null
echo "firewall refreshed: $(wc -l < "$TMP/cf-v4.txt") CF IPv4 ranges, $(wc -l < "$TMP/cf-v6.txt") IPv6"
