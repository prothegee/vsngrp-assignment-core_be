#!/bin/bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

echo "run-tests-db-integrity: restoring local dotnet tools"
if ! dotnet tool restore; then
    echo "run-tests-db-integrity: FAIL, dotnet tool restore failed, dotnet-ef is not available"
    exit 1
fi

POSTGRES_CONTAINER="vsngrp-core-be-db-integrity-postgres"
REDIS_CONTAINER="vsngrp-core-be-db-integrity-redis"
POSTGRES_IMAGE="postgres:18"
REDIS_IMAGE="redis:8"

cleanup() {
    echo "run-tests-db-integrity: cleaning up"
    docker stop "$POSTGRES_CONTAINER" "$REDIS_CONTAINER" >/dev/null 2>&1 || true
    docker rm "$POSTGRES_CONTAINER" "$REDIS_CONTAINER" >/dev/null 2>&1 || true
    docker rmi "$POSTGRES_IMAGE" "$REDIS_IMAGE" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker rm -f "$POSTGRES_CONTAINER" "$REDIS_CONTAINER" >/dev/null 2>&1 || true

echo "run-tests-db-integrity: starting a default postgres container"
docker run -d --name "$POSTGRES_CONTAINER" \
    -e POSTGRES_USER=integrity \
    -e POSTGRES_PASSWORD=integrity \
    -e POSTGRES_DB=vsngrp_core_be_integrity \
    -p 127.0.0.1::5432 \
    "$POSTGRES_IMAGE" >/dev/null

echo "run-tests-db-integrity: starting a default redis container"
docker run -d --name "$REDIS_CONTAINER" \
    -p 127.0.0.1::6379 \
    "$REDIS_IMAGE" >/dev/null

POSTGRES_PORT=$(docker port "$POSTGRES_CONTAINER" 5432/tcp | head -1 | cut -d: -f2)
REDIS_PORT=$(docker port "$REDIS_CONTAINER" 6379/tcp | head -1 | cut -d: -f2)

echo "run-tests-db-integrity: waiting for postgres on port ${POSTGRES_PORT}"
until docker exec "$POSTGRES_CONTAINER" pg_isready -U integrity >/dev/null 2>&1; do
    sleep 1
done

echo "run-tests-db-integrity: waiting for redis on port ${REDIS_PORT}"
until docker exec "$REDIS_CONTAINER" redis-cli ping 2>/dev/null | grep -q PONG; do
    sleep 1
done

CONNECTION_STRING="Host=localhost;Port=${POSTGRES_PORT};Database=vsngrp_core_be_integrity;Username=integrity;Password=integrity"

echo "run-tests-db-integrity: applying migrations"
MIGRATION_LOG=$(mktemp)
if MIGRATIONS_CONNECTION_STRING="$CONNECTION_STRING" dotnet ef database update --project src > "$MIGRATION_LOG" 2>&1; then
    if grep -q "__EFMigrationsHistory" "$MIGRATION_LOG"; then
        printf '\033[33mrun-tests-db-integrity: note, EF Core probed for the migrations history table on this fresh database and logged a "Failed executing DbCommand" line for it, that is expected on a first run against an empty database, not a real error\033[0m\n'
    fi
    echo "run-tests-db-integrity: migrations applied"
    rm -f "$MIGRATION_LOG"
else
    echo "run-tests-db-integrity: FAIL, migrations did not apply"
    cat "$MIGRATION_LOG"
    rm -f "$MIGRATION_LOG"
    exit 1
fi

echo "run-tests-db-integrity: checking accounts table schema"
COLUMN_COUNT=$(docker exec "$POSTGRES_CONTAINER" psql -U integrity -d vsngrp_core_be_integrity -tAc \
    "SELECT count(*) FROM information_schema.columns WHERE table_name = 'accounts' AND column_name IN ('Id', 'Email', 'PasswordHash', 'CreatedAt');")
if [ "$COLUMN_COUNT" -ne 4 ]; then
    echo "run-tests-db-integrity: FAIL, expected 4 known columns on accounts, found ${COLUMN_COUNT}"
    exit 1
fi
echo "run-tests-db-integrity: accounts table schema ok"

echo "run-tests-db-integrity: checking unique index on Email"
UNIQUE_INDEX_COUNT=$(docker exec "$POSTGRES_CONTAINER" psql -U integrity -d vsngrp_core_be_integrity -tAc \
    "SELECT count(*) FROM pg_indexes WHERE tablename = 'accounts' AND indexdef ILIKE '%UNIQUE%' AND indexdef ILIKE '%Email%';")
if [ "$UNIQUE_INDEX_COUNT" -lt 1 ]; then
    echo "run-tests-db-integrity: FAIL, no unique index found on accounts.Email"
    exit 1
fi
echo "run-tests-db-integrity: unique index on Email ok"

echo "run-tests-db-integrity: checking the unique constraint is enforced"
docker exec "$POSTGRES_CONTAINER" psql -U integrity -d vsngrp_core_be_integrity -c \
    "INSERT INTO accounts (\"Id\", \"Email\", \"PasswordHash\", \"CreatedAt\") VALUES (gen_random_uuid(), 'integrity@vsngrp.dev', 'x', now());" >/dev/null
if docker exec "$POSTGRES_CONTAINER" psql -U integrity -d vsngrp_core_be_integrity -c \
    "INSERT INTO accounts (\"Id\", \"Email\", \"PasswordHash\", \"CreatedAt\") VALUES (gen_random_uuid(), 'integrity@vsngrp.dev', 'x', now());" >/dev/null 2>&1; then
    echo "run-tests-db-integrity: FAIL, duplicate email was accepted"
    exit 1
fi
echo "run-tests-db-integrity: duplicate email correctly rejected"

echo "run-tests-db-integrity: checking redis read, write, and ttl"
docker exec "$REDIS_CONTAINER" redis-cli set integrity-probe hello EX 5 >/dev/null
VALUE=$(docker exec "$REDIS_CONTAINER" redis-cli get integrity-probe)
if [ "$VALUE" != "hello" ]; then
    echo "run-tests-db-integrity: FAIL, redis did not return the value it was given"
    exit 1
fi
TTL=$(docker exec "$REDIS_CONTAINER" redis-cli ttl integrity-probe)
if [ "$TTL" -le 0 ]; then
    echo "run-tests-db-integrity: FAIL, redis ttl was not set"
    exit 1
fi
echo "run-tests-db-integrity: redis read, write, and ttl ok"

echo "run-tests-db-integrity: all checks passed"
