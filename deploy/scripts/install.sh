#!/usr/bin/env bash
set -euo pipefail
# shellcheck source=common.sh
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

service_uid=10001

if [[ $EUID -ne 0 ]]; then
  echo "Run this script with sudo." >&2
  exit 1
fi

install -d -m 755 "$CIVIC_LENS_ROOT" "$CIVIC_LENS_ROOT/config"
install -d -m 700 "$CIVIC_LENS_ROOT/env"
install -d -m 755 -o "$service_uid" -g "$service_uid" "$CIVIC_LENS_ROOT/captures" "$CIVIC_LENS_ROOT/releases"
install -d -m 700 -o "$service_uid" -g "$service_uid" \
  "$CIVIC_LENS_ROOT/review-keys" "$CIVIC_LENS_ROOT/postgres" \
  "$CIVIC_LENS_ROOT/postgres/operational" "$CIVIC_LENS_ROOT/postgres/search"

for template in "$deploy_directory"/env/*.env.example; do
  target="$CIVIC_LENS_ROOT/env/$(basename "${template%.example}")"
  if [[ -e $target ]]; then
    echo "Kept $target"
  else
    install -m 600 "$template" "$target"
    echo "Created $target"
  fi
done

echo "Edit the files in $CIVIC_LENS_ROOT/env and add collection.json and issues.json to $CIVIC_LENS_ROOT/config."
