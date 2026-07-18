# Low-Level Design: Core BE

<br>

## Project structure

```
vsngrp-assignment-core_be
|
|___/src
|   |___/Controllers
|   |   |___AuthController.cs         (signup, signin, signout, refresh)
|   |
|   |___/Services
|   |   |___AccountService.cs         (signup/signin against Postgres, write/read routing)
|   |   |___JwtService.cs             (issues access tokens)
|   |   |___SessionService.cs         (Redis session and refresh token records)
|   |   |___SessionAuthorizationHandler.cs  (checks the sid claim against Redis)
|   |
|   |___/Models
|   |   |___Account.cs
|   |   |___AppConfig.cs              (config.json binding)
|   |   |___AuthRequests.cs
|   |   |___AuthResponses.cs
|   |
|   |___/Data
|   |   |___AppDbContext.cs
|   |   |___AppDbContextFactory.cs    (CreateWrite / CreateRead)
|   |   |___/Migrations
|   |
|   |___Program.cs
|   |___VsngrpCoreBe.csproj
|
|___/tests
|   |___/Unit
|   |   |___JwtServiceTests.cs
|   |   |___AccountServiceTests.cs
|   |   |___AccountServiceTestFixture.cs
|   |
|   |___/Integration
|   |   |___AuthEndpointsTests.cs     (real HTTP client, Testcontainers Postgres + Redis)
|   |   |___AuthEndpointsTestFixture.cs
|   |   |___CoreBeWebApplicationFactory.cs
|   |
|   |___VsngrpCoreBe.Tests.csproj
|
|___/containers
|   |___/postgres-primary
|   |   |___/init                     (creates the replication role)
|   |   |___/data                     (bind mount, gitignored)
|   |
|   |___/postgres-replica
|   |   |___Dockerfile                (runs pg_basebackup on first start)
|   |   |___replica-entrypoint.sh
|   |   |___/data                     (bind mount, gitignored)
|   |
|   |___/redis
|   |   |___/data                     (bind mount, gitignored)
|   |
|   |___docker-compose.yml            (Postgres 18 primary + replica, Redis 8, 127.0.0.1-bound)
|
|___/config
|   |___config.json.template
|
|___/.github
|   |___/workflows
|       |___ci.yml
|       |___cd.yml
|
|___VsngrpCoreBe.slnx
|___Directory.Build.props
|___dotnet-tools.json
|___Dockerfile
|___containers.sh
|___debug.sh
|___run-tests.sh
|___run-tests-db-integrity.sh
|___verify-deploy.sh
|___.dockerignore
|___.gitignore
|___README.md
|___hld.md
|___lld.md
|___adr.md
|___development.md
|___deployment.md
|___LICENSE
```

<br>

## Scripts

| Script | Purpose |
| :- |
| `containers.sh` | datastore lifecycle: `up`, `down`, `start`, `stop`, `cleanup` (destructive, requires typed `YES`) |
| `debug.sh` | local dev entrypoint, warns and copies the config template if `config/config.json` is missing, starts the datastore containers if they are not already running, then runs the service |
| `run-tests.sh` | runs `dotnet test` (unit, edge, integration), then `run-tests-db-integrity.sh` |
| `run-tests-db-integrity.sh` | standalone database sanity check, plain Postgres and Redis containers separate from `containers/`, torn down afterward |
| `verify-deploy.sh` | post-deploy smoke test, TLS, `/health`, and CORS, run from `cd.yml` |

<br>

## Config schema (`config/config.json`)

| Field | Type | Notes |
| :- |
| `port` | number | Kestrel binds to `0.0.0.0:<port>` |
| `version` | string | semver, manually bumped per release, read by `/health` |
| `jwtSecret` | string | HS256 shared secret, must match Core BE WS |
| `postgres.write.connectionString` | string | primary, used for signup |
| `postgres.read.connectionString` | string | replica, used for signin lookup |
| `redis.connectionString` | string | session storage |
| `corsAllowedOrigins` | string array | must include the Core FE origin |

<br>

## Database schema

Single table, `accounts`:

| Column | Type | Notes |
| :- |
| `Id` | uuid | primary key |
| `Email` | text | unique index, enforced by the database |
| `PasswordHash` | text | produced by `PasswordHasher<Account>` |
| `CreatedAt` | timestamptz | set at signup |

Signup writes go through `postgres.write` (the primary). A duplicate email is not pre-checked with a read: the insert is attempted directly and a unique-constraint violation from Postgres is caught and turned into a `409 Conflict`. This is correct even under replication lag, since the database itself is the source of truth for uniqueness. Signin reads go through `postgres.read` (the replica).

<br>

## Redis key schema

| Key | Value | TTL | Written by |
| :- |
| `session:<sid>` | accountId | refresh token lifetime (7 days) | signin, refresh (extended), deleted on signout |
| `refresh:<sha256(refreshToken)>` | sid | refresh token lifetime (7 days) | signin, refresh (rotated) |

The refresh token itself is never stored in Redis, only its hash. `sid` is a random value, not derived from the account, so it cannot be guessed from the account id.

<br>

## JWT claims

Access tokens are HS256, 15 minute lifetime:

| Claim | Meaning |
| :- |
| `sub` | account id |
| `sid` | session id, checked against Redis on every protected route |
| `exp` | expiry |

A valid signature and an unexpired token are not enough to access a protected route: `SessionAuthorizationHandler` also checks that `session:<sid>` still exists in Redis. This is what makes signout immediate instead of waiting for the token to expire naturally.

<br>

## Refresh flow

1. Client sends the `refresh_token` cookie to `POST /auth/refresh`.
2. Server hashes it and looks up `refresh:<hash>`. Missing means the token was already rotated or never existed, `401`.
3. Server confirms `session:<sid>` still exists. Missing means the account signed out, `401`.
4. Old `refresh:<hash>` is deleted, a new refresh token and access token are issued, `session:<sid>`'s TTL is extended.

Reusing an old, already-rotated refresh token always fails at step 2, even if it has not technically expired yet.
