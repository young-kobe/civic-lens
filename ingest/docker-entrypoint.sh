#!/bin/sh
# civic-ingest resolves migrations as $(dirname dbPath)/migrations. On the
# pre-container host layout that was a symlink into /opt/civic-lens; inside
# the container the repo is not mounted, so materialize the image's copy of
# the migrations beside the DB before handing off to the binary.
set -eu

if [ -n "${CIVIC_DB_PATH:-}" ]; then
    db_dir=$(dirname "$CIVIC_DB_PATH")
    mig_dir="$db_dir/migrations"
    mkdir -p "$db_dir"
    # Replace a leftover host symlink (pre-container layout) with a real dir.
    if [ -L "$mig_dir" ]; then
        rm "$mig_dir"
    fi
    mkdir -p "$mig_dir"
    cp /app/data/migrations/*.sql "$mig_dir"/
fi

exec /app/civic-ingest "$@"
