#!/usr/bin/env bash
# Usage: sudo deploy/scripts/install-operator-access.sh <agent SSH public key, in quotes>
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

if [[ $EUID -ne 0 ]]; then
  echo "Run this script with sudo." >&2
  exit 1
fi
public_key=${1:?Give the agent SSH public key, in quotes.}
if [[ ! $public_key =~ ^(ssh-ed25519\ [A-Za-z0-9+/=]+)(\ .*)?$ ]]; then
  echo "Give one ssh-ed25519 public key." >&2
  exit 1
fi
key=${BASH_REMATCH[1]}
agent_user=civic-lens-agent

# The two commands run scripts from the checkout as root, so only root may change them.
repository=$(dirname "$deploy_directory")
writable=$(find "$repository" \( -type l -o ! -user root -o -perm /022 \) -print -quit)
if [[ -n $writable || -L $repository || $repository != /opt/civic-lens || -n $(find /opt -maxdepth 0 \( ! -user root -o -perm /022 \) -print) ]]; then
  echo "Refused. Root must own /opt and /opt/civic-lens, only root may write there, and the checkout holds no links. First problem: ${writable:-/opt or the path}" >&2
  echo "To correct a checkout: sudo chown -R root:root /opt/civic-lens; sudo chmod -R go-w /opt/civic-lens" >&2
  exit 1
fi
compose exec -T postgres sh -c 'psql --no-psqlrc --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"' \
  < "$deploy_directory/scripts/readonly-grants.sql"

install -o root -g root -m 755 "$deploy_directory/scripts/readonly-sql.sh" /usr/local/bin/civic-lens-sql
install -o root -g root -m 755 "$deploy_directory/scripts/operator-command.sh" /usr/local/bin/civic-lens-ops
install -o root -g root -m 755 "$deploy_directory/scripts/agent-forced-command.sh" /usr/local/bin/civic-lens-agent

id "$agent_user" >/dev/null 2>&1 || useradd --create-home --shell /bin/bash "$agent_user"
home=$(getent passwd "$agent_user" | cut -d: -f6)
chown root:"$agent_user" "$home"
chmod 750 "$home"
install -d -o root -g "$agent_user" -m 750 "$home/.ssh"
printf 'restrict,command="/usr/local/bin/civic-lens-agent" %s\n' "$key" > "$home/.ssh/authorized_keys"
chown root:"$agent_user" "$home/.ssh/authorized_keys"
chmod 640 "$home/.ssh/authorized_keys"

rule=$(mktemp)
trap 'rm -f "$rule"' EXIT
printf '%s ALL=(root) NOPASSWD: /usr/local/bin/civic-lens-sql "", /usr/local/bin/civic-lens-ops\n' "$agent_user" > "$rule"
visudo --check --file="$rule" >/dev/null
install -o root -g root -m 440 "$rule" /etc/sudoers.d/civic-lens-operator

echo "Installed. Only the key you gave can reach these commands."
