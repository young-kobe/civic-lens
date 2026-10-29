# shellcheck shell=bash
# Sourced by the other scripts. It owns the default root and the compose call.
CIVIC_LENS_ROOT=${CIVIC_LENS_ROOT:-/srv/civic-lens}
export CIVIC_LENS_ROOT
deploy_directory=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)

compose() {
  docker compose --file "$deploy_directory/compose.yaml" --env-file "$CIVIC_LENS_ROOT/env/compose.env" "$@"
}
