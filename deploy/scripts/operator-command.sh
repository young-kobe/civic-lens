#!/usr/bin/env bash
# Runs one listed production command as root, so the agent key needs no general sudo, docker, or env file access.
# A deploy takes only a newer commit on origin/main, because the checkout's scripts then run as root and a migration has no undo.
set -euo pipefail
export HOME=/root PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

lock_file=/run/civic-lens-ops.lock
repository=/opt/civic-lens
compose=$repository/deploy/scripts/compose.sh
git_root=(git -c "safe.directory=$repository" -C "$repository")

refuse() {
  echo "Refused. Listed commands:" >&2
  sed -n 's/^ *# usage: //p' "$0" >&2
  exit 2
}

number() { [[ ${1:-} =~ ^[1-9][0-9]{0,3}$ ]] || refuse; }

deploy() {
  [[ ${1:-} =~ ^[0-9a-f]{40}$ ]] || refuse
  "${git_root[@]}" fetch --quiet origin main 9>&-
  if ! "${git_root[@]}" merge-base --is-ancestor "$1" origin/main; then
    echo "$1 is not on origin/main." >&2
    exit 1
  fi
  if ! "${git_root[@]}" merge-base --is-ancestor HEAD "$1"; then
    echo "$1 is older than the checkout. A rollback stays with the owner." >&2
    exit 1
  fi
  "${git_root[@]}" checkout --quiet --detach "$1"
  "$repository/deploy/scripts/update.sh" "$1"
  cd "$repository"
  deploy/scripts/check-live.sh
}

publish() {
  [[ ${1:-} =~ ^[A-Za-z0-9._-]{1,128}$ ]] || refuse
  local key=$1 option
  shift
  for option in "$@"; do
    [[ $option == --rewrite-entries || $option == --rewrite-officials ]] || refuse
  done
  "$compose" run --rm cli releases publish "$key" "$@"
}

if [[ $* != status && $* != "worker health" && $* != "release sizes" ]]; then
  [[ -e $lock_file ]] || install -m 600 /dev/null "$lock_file"
  exec 9>"$lock_file"
  if ! flock --nonblock 9; then
    echo "Another listed command is running. Wait for it to finish." >&2
    exit 1
  fi
fi

case "$*" in
  # usage: status
  status) "$compose" ps --all ;;
  # usage: release sizes
  "release sizes") cd /srv/civic-lens/releases/releases; du -sB1 [0-9]* ;;
  # usage: deploy <full commit SHA on origin/main>
  "deploy "*) [[ $# -eq 2 ]] || refuse; deploy "$2" ;;
  # usage: db migrate
  "db migrate") "$compose" run --rm cli db migrate ;;
  # usage: worker health
  "worker health") "$compose" run --rm cli worker health ;;
  # usage: votes draft redraft
  "votes draft redraft") "$compose" run --rm cli votes draft redraft ;;
  # usage: actions draft redraft
  "actions draft redraft") "$compose" run --rm cli actions draft redraft ;;
  # usage: bills subjects backfill <count>
  "bills subjects backfill "*)
    [[ $# -eq 4 ]] || refuse
    number "$4"
    "$compose" run --rm cli bills subjects backfill "$4"
    ;;
  # usage: releases publish <key> [--rewrite-entries] [--rewrite-officials]
  "releases publish "*) shift 2; publish "$@" ;;
  *) refuse ;;
esac
