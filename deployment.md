# Deployment Guide: Core BE

<br>

## Overview

Core BE runs on the same AWS EC2 instance as Core BE WS and Core FE, each as its own `docker compose` project. A single shared reverse-proxy container (`vsngrp-reverse-proxy`, `network_mode: host`) owns ports 80 and 443 and is the only process reachable from outside the instance, it terminates TLS and reverse-proxies to each service's app container on `127.0.0.1`. This service owns and deploys its own server block, `nginx/vsngrp-bec.conf`, see Deploy flow below and `tasks.md` Deployment infrastructure for the shared proxy itself.

Core BE's own containers (Postgres primary, Postgres replica, Redis) bind to `127.0.0.1` only and are never reachable from outside the instance, not even from the other two services' containers.

<br>

## Ports

| Port | Reachable from | Purpose |
| :- |
| `80` | public internet | certbot ACME challenge, redirects to `443` |
| `443` | public internet | the only public entry point, TLS terminates here |
| `9001` | `127.0.0.1` on the EC2 instance only | Core BE's own Kestrel process, never exposed directly |
| `5432` / `5433` | `127.0.0.1` on the EC2 instance only | Postgres primary / replica |
| `6379` | `127.0.0.1` on the EC2 instance only | Redis |

The EC2 security group only opens `22` (SSH), `80`, and `443`. Everything else, including `9001` and every datastore port, is closed to the public internet at the security group level as well as bound to loopback at the container level, two layers protecting the same thing.

<br>

## One-time server setup

1. Clone this repository to the server, on the `main-stable` branch.
2. No manual `config.json` step needed, `cd.yml` creates it from `config/config.json.template` on the first deploy and seeds it from the `CORE_BE_SED_*` secrets below (see GitHub Actions secrets and Deploy flow).
3. No manual datastore step needed either, `cd.yml` runs `./containers.sh up` on every deploy, which brings up Postgres primary, Postgres replica, and Redis if they are not already running, and fails the deploy immediately if any of them do not stay running afterward.
4. No manual migration step needed, `cd.yml` runs `dotnet ef database update` automatically on every deploy (see ADR 021).
5. Confirm the shared reverse proxy (`vsngrp-reverse-proxy`) is already up and its certificate for `vsngrp-bec.prothegee.dev` already issued, this is separate, shared infra provisioned once, see `tasks.md` Deployment infrastructure, not a per-service step. From here on, this service's own server block (`nginx/vsngrp-bec.conf`, committed in this repo) deploys into it automatically on every `cd.yml` run, no manual nginx or certbot step needed per service.

<br>

## GitHub Actions secrets

`cd.yml` needs these repository secrets configured before it can deploy:

| Secret | Value |
| :- |
| `EC2_HOST` | the EC2 instance's address |
| `EC2_SSH_USER` | the SSH user used for deploys |
| `EC2_SSH_KEY` | the private half of a deploy key, the matching public key must be authorized on the instance |
| `CORE_BE_DEPLOY_PATH` | absolute path to this repository's clone on the instance |
| `CORE_BE_CONFIG_PATH` | absolute path to the real `config.json` on the instance, mounted read-only into the container |
| `PROXY_CONF_D_PATH` | absolute path to the shared reverse proxy's `conf.d` folder on the instance, this service's own `nginx/vsngrp-bec.conf` is copied there on every deploy |
| `CORE_BE_SED_CONFIG_JWT_SECRET` | the shared HS256 secret, must match the value used for Core BE WS |
| `CORE_BE_SED_CONFIG_PG_WRITE` | the primary Postgres connection string, host must be `vsngrp-core-be-postgres-primary`, port `5432` |
| `CORE_BE_SED_CONFIG_PG_READ` | the replica Postgres connection string, host must be `vsngrp-core-be-postgres-replica`, port `5432` (not `5433`, that is only the host-external port mapping, containers reach each other on Postgres's real internal port), password must be identical to `CORE_BE_SED_CONFIG_PG_WRITE`'s, Postgres streaming replication mirrors the primary's users and passwords, the replica cannot have a different one |
| `CORE_BE_SED_CONFIG_RD` | the Redis connection string, `vsngrp-core-be-redis:6379,password=...`, requires a password in production (see ADR 019) |
| `CORE_BE_SED_ALLOWED_ORIGINS` | the `corsAllowedOrigins` JSON array, `["https://vsngrp-fec.prothegee.dev"]` in prod |

The host in every Postgres/Redis connection string must be the container name from `containers/docker-compose.yml`, not `127.0.0.1`, the app container runs on its own Docker network, not host networking, so `127.0.0.1` inside it means itself. `config.json` is regenerated from `config/config.json.template` and the `CORE_BE_SED_*` secrets on every single deploy, not just the first one, it is a fully derived file, never hand-edited on the instance. If it is still not valid JSON after seeding, the secrets themselves contain invalid JSON syntax, `cd.yml` fails the deploy immediately rather than mounting a broken config into the container. `cd.yml` also extracts the password portion of `CORE_BE_SED_CONFIG_PG_WRITE` and exports it as `POSTGRES_PASSWORD` before `./containers.sh up`, so the datastore container's own password and the app's connection password always come from the same secret. It does the same for `CORE_BE_SED_CONFIG_RD`, extracting the part after `password=` and exporting it as `REDIS_PASSWORD` (see ADR 019). To rotate the Postgres password, it must actually be changed inside the running database (`ALTER USER core_be WITH PASSWORD '...'`), then both `CORE_BE_SED_CONFIG_PG_WRITE` and `CORE_BE_SED_CONFIG_PG_READ` updated to match, `POSTGRES_PASSWORD` only takes effect the first time a Postgres data directory is initialized, changing the secret alone does not change an already-initialized database's password. Redis's password can just be changed live with `redis-cli CONFIG SET requirepass ...`, but that alone does not persist across a container recreation, `REDIS_PASSWORD` also needs updating to match.

<br>

## Deploy flow

1. Open a pull request into `main`. `ci.yml` must pass (build, lint, tests, Docker build check).
2. Once `main` is green and ready to ship, promote it into `main-stable`, either by merging a pull request from `main` into `main-stable`, or by pushing directly to `main-stable`.
3. Any push to `main-stable` triggers `cd.yml` (a PR merge is itself a push under the hood, so both paths use the same trigger), which connects over SSH and:
   - checks that `PROXY_CONF_D_PATH` (the shared reverse proxy's `conf.d` folder) exists, and fails the deploy immediately if it does not
   - pulls the latest `main-stable`
   - brings up this service's own datastore containers (`./containers.sh up`)
   - applies EF Core migrations against the primary, unconditionally, every deploy (see ADR 021)
   - regenerates `CORE_BE_CONFIG_PATH` from `config/config.json.template` every single deploy, seeding `jwtSecret`, `postgres.write`, `postgres.read`, `redis`, and `corsAllowedOrigins` from the `CORE_BE_SED_*` secrets
   - fails the deploy immediately if the config is not valid JSON after seeding
   - checks that `corsAllowedOrigins` in that config includes the production Core FE origin (`https://vsngrp-fec.prothegee.dev`), and fails the deploy immediately if it does not
   - builds the image with `--build-arg GIT_SHA=$(git rev-parse --short HEAD)`
   - stops and replaces the running app container
   - copies this service's own `nginx/vsngrp-bec.conf` into `PROXY_CONF_D_PATH` and reloads the `vsngrp-reverse-proxy` container
   - runs `verify-deploy.sh`

The config file is never baked into the image. It is mounted read-only from the path in `CORE_BE_CONFIG_PATH` at container start. Every deploy regenerates it fresh from the current secrets, it is never hand-edited or preserved across deploys, the secrets are the only source of truth.

<br>

## Verifying a deploy manually

```
./verify-deploy.sh "" /path/to/config.json
```

This checks the TLS certificate is valid, `GET /health` reports the expected `version` and `gitSha`, the CORS allowlist includes the production Core FE origin, and that the Postgres write, Postgres read, and Redis connection strings inside `config.json` actually authenticate against the live containers (see ADR 020), not just that the file is valid JSON. It cannot confirm that ports `9001` and the datastore ports are actually unreachable from outside the instance, that check only means something run from an external machine and stays a manual step.
