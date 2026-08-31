#!/bin/sh

set -u

postgres_pid=""

create_backup() {
  timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
  backup_file="/backups/${POSTGRES_DB}-${timestamp}.sql"
  temporary_file="${backup_file}.tmp"

  echo "Creating PostgreSQL shutdown backup ${backup_file}"

  if PGPASSWORD="${POSTGRES_PASSWORD}" pg_dump \
    --host 127.0.0.1 \
    --username "${POSTGRES_USER}" \
    --dbname "${POSTGRES_DB}" \
    --clean \
    --if-exists \
    --no-owner \
    --no-privileges \
    --file "${temporary_file}"
  then
    mv "${temporary_file}" "${backup_file}"
    echo "PostgreSQL shutdown backup completed"
  else
    backup_status=$?
    rm -f "${temporary_file}"
    echo "PostgreSQL shutdown backup failed with status ${backup_status}" >&2
  fi
}

stop_postgres() {
  trap - TERM INT

  if [ -n "${postgres_pid}" ] && kill -0 "${postgres_pid}" 2>/dev/null
  then
    if pg_isready --host 127.0.0.1 --username "${POSTGRES_USER}" --dbname "${POSTGRES_DB}" >/dev/null 2>&1
    then
      create_backup
    else
      echo "PostgreSQL is not ready; skipping shutdown backup" >&2
    fi

    # SIGINT asks PostgreSQL for a fast shutdown: active client sessions are
    # terminated and transactions are rolled back cleanly after the dump.
    kill -INT "${postgres_pid}"
    wait "${postgres_pid}"
    exit $?
  fi

  exit 0
}

trap stop_postgres TERM INT

/usr/local/bin/docker-entrypoint.sh "$@" &
postgres_pid=$!

wait "${postgres_pid}"
exit $?
