#!/usr/bin/env bash
# Usage: sudo deploy/scripts/install-operator-access.sh <login user>
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

if [[ $EUID -ne 0 ]]; then
  echo "Run this script with sudo." >&2
  exit 1
fi
login_user=${1:?Name the login user that may run these commands.}
id "$login_user" >/dev/null

compose exec -T postgres sh -c 'psql --no-psqlrc --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"' \
  < "$deploy_directory/scripts/readonly-grants.sql"

install -o root -g root -m 755 "$deploy_directory/scripts/readonly-sql.sh" /usr/local/bin/civic-lens-sql
install -o root -g root -m 755 "$deploy_directory/scripts/operator-command.sh" /usr/local/bin/civic-lens-ops

rule=$(mktemp)
trap 'rm -f "$rule"' EXIT
printf '%s ALL=(root) NOPASSWD: /usr/local/bin/civic-lens-sql "", /usr/local/bin/civic-lens-ops\n' "$login_user" > "$rule"
visudo --check --file="$rule" >/dev/null
install -o root -g root -m 440 "$rule" /etc/sudoers.d/civic-lens-operator

echo "Installed civic-lens-sql and civic-lens-ops."
