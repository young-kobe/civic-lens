#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

tag=${1:?Usage: update.sh <full commit SHA of the images>}
tag_file="$CIVIC_LENS_ROOT/env/compose.env"

if [[ ! $tag =~ ^[0-9a-f]{40}$ ]]; then
  echo "The tag must be a full 40 character commit SHA." >&2
  exit 1
fi
if ! grep -q '^CIVIC_LENS_IMAGE_TAG=' "$tag_file"; then
  echo "Run install.sh first. $tag_file has no CIVIC_LENS_IMAGE_TAG line." >&2
  exit 1
fi

repository=$(dirname "$deploy_directory")
checkout=$(git -c safe.directory="$repository" -C "$repository" rev-parse HEAD)
if [[ $checkout != "$tag" ]]; then
  echo "The checkout is at $checkout. Check out $tag first, because the compose file and the Caddyfile come from the checkout." >&2
  exit 1
fi
if [[ -n $(git -c safe.directory="$repository" -C "$repository" status --porcelain -- deploy) ]]; then
  echo "The deploy folder has changes that no commit holds. Remove them first." >&2
  exit 1
fi

export CIVIC_LENS_IMAGE_TAG=$tag
compose pull
compose run --rm cli db migrate
sed -i "s/^CIVIC_LENS_IMAGE_TAG=.*/CIVIC_LENS_IMAGE_TAG=$tag/" "$tag_file"
compose up --detach --remove-orphans
compose up --detach --force-recreate caddy
echo "Running images tagged $tag."
