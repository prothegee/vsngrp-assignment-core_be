#!/bin/bash
set -euo pipefail

DOMAIN="vsngrp-bec.prothegee.dev"
FE_ORIGIN="https://vsngrp-fec.prothegee.dev"
EXPECTED_GIT_SHA="${1:-$(git rev-parse --short HEAD)}"
CONFIG_PATH="${2:-}"
if [ -z "$CONFIG_PATH" ]; then
    echo "verify-deploy: FAIL, no config path given as \$2, cannot verify datastore auth"
    exit 1
fi

echo "verify-deploy: checking TLS certificate for ${DOMAIN}"
CERT_END_DATE=$(echo | openssl s_client -servername "$DOMAIN" -connect "${DOMAIN}:443" 2>/dev/null | openssl x509 -noout -enddate | cut -d= -f2)
CERT_END_EPOCH=$(date -d "$CERT_END_DATE" +%s)
NOW_EPOCH=$(date +%s)
if [ "$CERT_END_EPOCH" -le "$NOW_EPOCH" ]; then
    echo "verify-deploy: FAIL, TLS certificate for ${DOMAIN} is expired"
    exit 1
fi
echo "verify-deploy: TLS certificate valid until ${CERT_END_DATE}"

echo "verify-deploy: checking /health"
HEALTH_BODY=$(curl -sf "https://${DOMAIN}/health")
HEALTH_STATUS=$(echo "$HEALTH_BODY" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)
HEALTH_GIT_SHA=$(echo "$HEALTH_BODY" | grep -o '"gitSha":"[^"]*"' | cut -d'"' -f4)

if [ "$HEALTH_STATUS" != "ok" ]; then
    echo "verify-deploy: FAIL, /health status was '${HEALTH_STATUS}', expected 'ok'"
    exit 1
fi

if [ "$HEALTH_GIT_SHA" != "$EXPECTED_GIT_SHA" ]; then
    echo "verify-deploy: FAIL, /health gitSha was '${HEALTH_GIT_SHA}', expected '${EXPECTED_GIT_SHA}'"
    exit 1
fi
echo "verify-deploy: /health ok, gitSha matches ${EXPECTED_GIT_SHA}"

echo "verify-deploy: checking corsAllowedOrigins for ${FE_ORIGIN}"
CORS_HEADER=$(curl -s -o /dev/null -D - \
    -X OPTIONS "https://${DOMAIN}/auth/signin" \
    -H "Origin: ${FE_ORIGIN}" \
    -H "Access-Control-Request-Method: POST" \
    | grep -i "^access-control-allow-origin:" | tr -d '\r' | cut -d' ' -f2)

if [ "$CORS_HEADER" != "$FE_ORIGIN" ]; then
    echo "verify-deploy: FAIL, Access-Control-Allow-Origin was '${CORS_HEADER}', expected '${FE_ORIGIN}'"
    exit 1
fi
echo "verify-deploy: CORS allowlist ok"

echo "verify-deploy: checking Postgres write (primary) connection auth"
PG_WRITE_CONN=$(python3 -c "import json; print(json.load(open('$CONFIG_PATH'))['postgres']['write']['connectionString'])")
PG_WRITE_DB=$(echo "$PG_WRITE_CONN" | grep -o 'Database=[^;]*' | cut -d= -f2)
PG_WRITE_USER=$(echo "$PG_WRITE_CONN" | grep -o 'Username=[^;]*' | cut -d= -f2)
PG_WRITE_PASSWORD=$(echo "$PG_WRITE_CONN" | grep -o 'Password=.*' | cut -d= -f2-)
if ! docker exec -e PGPASSWORD="$PG_WRITE_PASSWORD" vsngrp-core-be-postgres-primary psql -U "$PG_WRITE_USER" -d "$PG_WRITE_DB" -c 'SELECT 1' >/dev/null 2>&1; then
    echo "verify-deploy: FAIL, Postgres write connection auth failed, config.json's password does not match the live primary"
    exit 1
fi
echo "verify-deploy: Postgres write connection auth ok"

echo "verify-deploy: checking Postgres read (replica) connection auth"
PG_READ_CONN=$(python3 -c "import json; print(json.load(open('$CONFIG_PATH'))['postgres']['read']['connectionString'])")
PG_READ_DB=$(echo "$PG_READ_CONN" | grep -o 'Database=[^;]*' | cut -d= -f2)
PG_READ_USER=$(echo "$PG_READ_CONN" | grep -o 'Username=[^;]*' | cut -d= -f2)
PG_READ_PASSWORD=$(echo "$PG_READ_CONN" | grep -o 'Password=.*' | cut -d= -f2-)
if ! docker exec -e PGPASSWORD="$PG_READ_PASSWORD" vsngrp-core-be-postgres-replica psql -U "$PG_READ_USER" -d "$PG_READ_DB" -c 'SELECT 1' >/dev/null 2>&1; then
    echo "verify-deploy: FAIL, Postgres read connection auth failed, config.json's password does not match the live replica"
    exit 1
fi
echo "verify-deploy: Postgres read connection auth ok"

echo "verify-deploy: checking Redis connection auth"
RD_CONN=$(python3 -c "import json; print(json.load(open('$CONFIG_PATH'))['redis']['connectionString'])")
RD_PASSWORD=$(echo "$RD_CONN" | sed -n 's/.*password=//p')
if ! docker exec vsngrp-core-be-redis redis-cli -a "$RD_PASSWORD" --no-auth-warning PING | grep -q PONG; then
    echo "verify-deploy: FAIL, Redis connection auth failed, config.json's password does not match the live Redis instance"
    exit 1
fi
echo "verify-deploy: Redis connection auth ok"

echo "verify-deploy: all checks passed"
