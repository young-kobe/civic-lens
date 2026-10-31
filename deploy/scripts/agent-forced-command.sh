#!/usr/bin/env bash
# The only program that the agent SSH key can run. It takes "sql" or "ops <listed command>".
set -euf
export PATH=/usr/sbin:/usr/bin:/sbin:/bin

read -r -a words <<< "${SSH_ORIGINAL_COMMAND:-}"
case ${words[0]:-} in
  sql)
    [[ ${#words[@]} -eq 1 ]] || { echo "sql takes no arguments. Send the SQL on stdin." >&2; exit 2; }
    exec sudo -n /usr/local/bin/civic-lens-sql
    ;;
  ops) exec sudo -n /usr/local/bin/civic-lens-ops "${words[@]:1}" ;;
  *)
    echo "Use: sql, or ops <listed command>." >&2
    exit 2
    ;;
esac
