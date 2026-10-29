#!/usr/bin/env bash
set -euo pipefail
# shellcheck source=common.sh
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

export CIVIC_LENS_IMAGE_TAG=$tag
compose pull
compose run --rm cli db migrate
compose up --detach --remove-orphans
sed -i "s/^CIVIC_LENS_IMAGE_TAG=.*/CIVIC_LENS_IMAGE_TAG=$tag/" "$tag_file"
echo "Running images tagged $tag."
