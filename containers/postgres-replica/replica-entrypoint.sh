#!/bin/bash
set -euo pipefail

if [ -z "$(ls -A "$PGDATA" 2>/dev/null)" ]; then
    export PGPASSWORD="$POSTGRES_REPLICATION_PASSWORD"

    until pg_basebackup -h "$PRIMARY_HOST" -U "$POSTGRES_REPLICATION_USER" -D "$PGDATA" -Fp -Xs -P -R; do
        echo "replica: waiting for primary to accept a base backup"
        sleep 2
    done

    chmod 0700 "$PGDATA"
fi

exec docker-entrypoint.sh postgres
