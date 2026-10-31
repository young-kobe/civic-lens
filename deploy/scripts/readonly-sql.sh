#!/usr/bin/env bash
# Runs SQL from stdin as civic_lens_readonly. A backslash is refused, because a psql command such as \! runs a shell.
set -euo pipefail

sql=$(cat)
if [[ $sql == *\\* ]]; then
  echo "This command refuses a backslash, because psql commands can run a shell." >&2
  exit 2
fi

printf '%s\n' "$sql" | /opt/civic-lens/deploy/scripts/compose.sh exec -T postgres sh -c \
  'exec psql --no-psqlrc --username=civic_lens_readonly --dbname="$POSTGRES_DB" --variable=ON_ERROR_STOP=1 --pset=pager=off'
