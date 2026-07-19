# Architecture Decision Records: Core BE

Each entry is a decision, the reason for it, and what was traded away.

<br>

## ADR 001: JWT signing: HS256 shared secret

Decision: sign access tokens with HS256 using a secret shared between Core BE and Core BE WS.

Reason: both services are equally first-party and trusted, so an asymmetric key pair (RS256) would add setup and rotation overhead without a real security benefit here.

<br>

## ADR 002: Session revoke: Redis-backed session id

Decision: every access token carries a `sid` claim, checked against a Redis record on every protected route.

Reason: a plain stateless JWT cannot be revoked before it expires. Checking Redis makes signout immediate instead of waiting out the 15 minute access token lifetime.

<br>

## ADR 003: Refresh token storage: httpOnly cookie

Decision: the refresh token is an httpOnly cookie, never exposed to frontend JavaScript. `SameSite=None; Secure` in production, `SameSite=Lax; Secure=false` in local development over plain `http://localhost`.

Reason: a cookie that JavaScript cannot read is not readable by an XSS payload either. Client-side storage (`localStorage`, a JS-readable cookie) does not have that property.

<br>

## ADR 004: Refresh rotation: rotate on every use

Decision: each call to `/auth/refresh` invalidates the refresh token that was used and issues a new one. Reusing an already-rotated token is rejected.

Reason: limits how much damage a leaked refresh token can do. A leaked token that is never used again is harmless, one that gets used stops the legitimate client's very next refresh, which is a visible signal something is wrong.

<br>

## ADR 005: Password hashing: `PasswordHasher<Account>`

Decision: use ASP.NET Core's built-in `PasswordHasher<TUser>` instead of adding a separate hashing library.

Reason: it ships in the shared framework already (no extra dependency), and its defaults (PBKDF2 with a random salt) are appropriate for this use case.

<br>

## ADR 006: Duplicate email handling: catch, do not pre-check

Decision: `SignupAsync` attempts the insert directly and catches a unique-constraint violation from Postgres, rather than reading first to check if the email exists.

Reason: a read-then-write pattern has a race window, two signups for the same email arriving close together could both pass the read check. Letting the database's own unique index be the source of truth is correct regardless of timing, and is one query instead of two on the common path.

<br>

## ADR 007: Postgres write/read split: native streaming replication

Decision: one primary and one streaming replica, two connection strings (`postgres.write`, `postgres.read`), routed manually in `AccountService`.

Reason: the query surface here (signup, signin, refresh) is small enough that a routing proxy like PgPool-II would be unneeded operational overhead for a service this size.

<br>

## ADR 008: MSBuild workload resolver: disabled repository-wide

Decision: `Directory.Build.props` at the repository root sets `MSBuildEnableWorkloadResolver` to `false`.

Reason: this service has no workload dependencies at all, it is a plain ASP.NET Core Web API. The workload resolver still runs on every build regardless, and a machine with an incomplete or corrupted workload manifest install fails every single `dotnet build`, `dotnet run`, `dotnet test`, and `dotnet ef` call with an unrelated `MSB4242` SDK resolver error, even though nothing about this project actually needs it. Disabling it removes a class of environment-specific failures for a feature this project never uses.

<br>

## ADR 009: `dotnet-ef` as a local tool, not a global install

Decision: `dotnet-tools.json` at the repository root declares `dotnet-ef` as a local tool, restored with `dotnet tool restore`.

Reason: a global install (`dotnet tool install --global dotnet-ef`) only works if the shell's `PATH` includes the global tools directory, which is not guaranteed on every machine and was the direct cause of `run-tests-db-integrity.sh` failing with `Could not execute because the specified command or file was not found`. A local tool is restored into the project itself and resolved automatically by a bare `dotnet ef` call from the repository root, with no `PATH` setup required, in local development, CI, or anywhere else this repository is cloned.

<br>

## ADR 010: `containers.sh cleanup`: typed confirmation, container-based wipe

Decision: `cleanup` is a separate, explicit subcommand of `containers.sh`, not something `down` or any other command does implicitly. It prints an all-caps warning naming every directory it will touch, and only proceeds if the user types the literal word `YES`. The wipe itself runs inside a throwaway container against the mounted data directory, rather than a plain `rm -rf` on the host.

Reason: deleting a database's data directory is irreversible, and irreversible actions should never be one command away from an accidental keystroke, a distinct name plus a typed confirmation (not a `y/N` prompt that a stray Enter key can satisfy) makes it deliberate. The container-based wipe exists because Postgres and Redis create files under their own container user id, under a rootless engine like Podman that id does not map back to the host user, so a plain `rm` fails with a permission error even when the parent directory itself is world-writable, a throwaway container deletes its own files without that mismatch.

<br>

## ADR 011: Missing config: warn locally, seed in deployment

Decision: `debug.sh` treats a missing `config/config.json` as recoverable, it copies the template and warns. `cd.yml` treats a missing config file on the server the same way: it copies `config/config.json.template` and seeds `jwtSecret`, `postgres.write`, `postgres.read`, `redis`, and `corsAllowedOrigins` from GitHub Actions secrets (`CORE_BE_SED_*`), instead of failing the deploy.

Reason: locally, a fresh checkout with no config file yet is the normal first-run state, auto-copying the template with a clear warning gets a new contributor running faster than a bare crash would. Production has the same first-run case exactly once, the very first deploy to a fresh instance, and by then the real secrets already live in GitHub Actions, so seeding the file automatically there too removes a manual SSH step that would otherwise block every first deploy. Mounting a file that does not exist does not fail loudly either way, Docker silently creates an empty directory at that path instead, which only surfaces later as a confusing crash inside the container, creating the file before `docker run` avoids that.

Update, see ADR 017 and ADR 018: `cd.yml` now regenerates `config.json` on every deploy, not just the first one, this paragraph's original "never touches an existing file again" behavior no longer applies.

<br>

## ADR 012: Health check versioning: two separate signals

Decision: `/health` reports both `version` (semver, manually bumped per release) and `gitSha` (automatic, from a `GIT_SHA` Docker build-arg).

Reason: `version` answers "which release is this," `gitSha` answers "which exact commit is this." A release can span multiple commits during testing, and a hotfix commit might land between releases, the two questions need two separate answers.

<br>

## ADR 013: Datastore project isolation: named Compose project

Decision: `containers/docker-compose.yml` declares an explicit top-level `name: vsngrp-core-be`. `cd.yml`'s deploy step attaches the app container to `vsngrp-core-be_default` (this stack's own network) rather than a bare `containers_default`.

Reason: Compose derives a default project name from the directory holding the compose file, and both this service and Core BE WS name that directory `containers`. Without an explicit name, both stacks resolved to the same default project and the same default network, so bringing one service's stack up or down could interfere with the other's already-running containers. This surfaced while first bringing both services' stacks up together during Core BE WS's implementation, exactly the "Core BE, then Core BE WS" local dev order `tasks.md` already describes as the normal case.

<br>

## ADR 014: Local dev environment: `debug.sh` sets `ASPNETCORE_ENVIRONMENT=Development`

Decision: `debug.sh` now runs `ASPNETCORE_ENVIRONMENT=Development dotnet run` instead of a bare `dotnet run`.

Reason: this service has no `launchSettings.json`, so a bare `dotnet run` defaults to the `Production` environment. `AuthController.BuildCookieOptions` issues the refresh cookie as `Secure` whenever `environment.IsDevelopment()` is false, and a `Secure` cookie is silently refused by a real browser over plain `http://localhost`, the browser never stores it at all. This went unnoticed through every prior verification pass because those used curl and wscat, neither of which enforces that browser-only rule, curl happily accepts and forwards a `Secure` cookie over `http://`. It only surfaced once Core FE connected a real browser tab to this service for the first time, at which point the refresh flow silently could not work: signin appeared to succeed, but no session survived a reload and no silent refresh could ever fire.

<br>

## ADR 015: Reverse proxy: containerized, this service owns and deploys its own conf file

Decision: the public-facing reverse proxy (`vsngrp-reverse-proxy`, `nginx:alpine`, `network_mode: host`) is a separate container this service does not own, but this repo commits and deploys its own server block, `nginx/vsngrp-bec.conf`, copied into the proxy's shared `conf.d` and reloaded on every `cd.yml` run.

Reason: `network_mode: host` lets the proxy container reach `127.0.0.1:9001` exactly like a host-installed nginx would, so nothing about this service's own port binding or Compose network needed to change to support it. Each service owning exactly 1 conf file, written by exactly 1 pipeline, keeps 3 independent deploy pipelines from racing on or overwriting a shared file, one truth source per domain even though the proxy itself is shared. Full detail in `tasks.md`'s Deployment infrastructure section.

<br>

## ADR 016: Datastore data directories: no `.gitkeep`, `containers.sh up` verifies containers actually stay running, restart policy set

Decision: `containers/postgres-primary/data/.gitkeep`, `containers/postgres-replica/data/.gitkeep`, and `containers/redis/data/.gitkeep` are removed and no longer tracked, `.gitignore` still ignores everything else under those directories. `containers.sh up` now checks every datastore container is actually in the `Running` state after `docker compose up -d`, printing that container's own recent logs and exiting non-zero if any of them are not. All 3 datastore services now also set `restart: unless-stopped`, matching the app container and the reverse proxy, none of them had any restart policy before.

Reason: the Redis image's entrypoint auto-fixes ownership on its mounted data directory at startup, but skips that step entirely the moment it finds any file it does not recognize there, exactly what a git-tracked `.gitkeep` is on an otherwise-empty fresh clone. That silently broke the very first real deploy: Redis crashed on a permission error trying to create its own append-only directory, seconds after `docker compose up -d` had already reported it "Started". `docker compose up -d` only confirms a container process launched, not that it stayed up, so the crash was invisible to `cd.yml`, which moved on to build and deploy the app anyway and reported the whole run green. The verification helper (`all_running`) already existed in this script but had never actually been wired into `up`.

<br>

## ADR 017: Postgres/Redis connection secrets stay, but the host must be the container name and the Postgres password is shared with the datastore container itself

Decision: `CORE_BE_SED_CONFIG_PG_WRITE`, `CORE_BE_SED_CONFIG_PG_READ`, and `CORE_BE_SED_CONFIG_RD` remain real secrets holding full connection strings, `cd.yml` reads and seeds all 3 exactly as before. Their host must be the actual container name (`vsngrp-core-be-postgres-primary`, `vsngrp-core-be-postgres-replica`, `vsngrp-core-be-redis`), not `127.0.0.1`, and the replica's port must be its own internal `5432`, not the host-external `5433`. `cd.yml` also extracts the password out of `CORE_BE_SED_CONFIG_PG_WRITE` (everything after `Password=`) and exports it as `POSTGRES_PASSWORD` before `./containers.sh up` runs, `containers/docker-compose.yml` reads it via `${POSTGRES_PASSWORD:-devpassword}`, so the datastore container's own password and the app's connection password always come from the same secret, never two independently-typed values that have to coincidentally agree.

<br>

## ADR 018: `config.json` regenerated every deploy, not seeded once

Decision: `cd.yml` now runs `seed_config` unconditionally on every deploy, superseding ADR 011's "seed only if the file is missing" behavior. `config.json` is fully derived from `config/config.json.template` plus the current `CORE_BE_SED_*` secrets, every single run, never hand-edited or preserved across deploys.

Reason: ADR 011's original design let a stale or wrong value survive silently until someone remembered to manually delete `config.json` on the instance and trigger a redeploy, exactly what happened repeatedly while chasing the Redis and Postgres connection bugs fixed in ADR 017, correcting a secret was not enough on its own, the already-seeded file kept the old value until manually deleted over SSH. Secrets are still the source of truth for these connection strings (see ADR 017), regenerating every deploy is what makes updating a secret actually take effect without a manual SSH step, hand-editing `config.json` directly on the instance is no longer a supported way to change a value, it would just get overwritten on the next deploy anyway.

Reason: the first real production deploy broke signup with a Redis connection failure, `config.json`'s `redis.connectionString` was `127.0.0.1:6379`, which is the app container's own loopback, not the host's, once the app runs on its own Docker network rather than host networking. Fixing that surfaced a second, worse bug: even with the right host, Postgres authentication itself failed, `CORE_BE_SED_CONFIG_PG_WRITE`'s password did not match `containers/docker-compose.yml`'s hardcoded `POSTGRES_PASSWORD: devpassword`, two values that were only ever kept in sync by a human remembering to update both, and had already drifted apart. Host, port, database name, and username are not actually secret, they are fixed by the already-committed compose file, encoding them as free-text secrets only added a way for them to be wrong. Redis had no password configured at first, see ADR 019 for why that changed. `POSTGRES_PASSWORD` only takes effect the first time Postgres initializes an empty data directory, changing it does not retroactively change an already-initialized database's password, that still needs an explicit `ALTER USER` against the running instance, or a fresh init against an empty data directory, if it is ever rotated for real.

<br>

## ADR 019: Redis requires a password in production, local dev stays password-free

Decision: `containers/docker-compose.yml`'s Redis service now takes `REDIS_PASSWORD` the same way Postgres takes `POSTGRES_PASSWORD`, an env var substituted at compose-parse time, defaulting to empty when unset. `command` runs `redis-server --appendonly yes --requirepass "$REDIS_PASSWORD"`, an empty value means `--requirepass ""`, which Redis treats as no auth required, so local `debug.sh`/`containers.sh up` behaves exactly as before. `cd.yml` extracts the password out of `CORE_BE_SED_CONFIG_RD` (everything after `password=`) and exports it as `REDIS_PASSWORD` before `./containers.sh up`, the same pattern ADR 017 already established for Postgres. `CORE_BE_SED_CONFIG_RD` itself now carries the password inline as `host:port,password=...`, the connection-string format `StackExchange.Redis` (the app's own Redis client) already parses natively.

Reason: sir wants every datastore, Postgres and Redis alike, to require real authentication in production, a passwordless Redis reachable by anyone who can reach its container network was a real gap, not a deliberate simplification. Restricting the requirement to production only (not local dev) matches how Postgres already worked, `devpassword` locally is a placeholder nobody depends on being secret, a real contributor running `debug.sh` should not need a GitHub secret just to start the app.

<br>

## ADR 020: `verify-deploy.sh` authenticates against every datastore, not just checks JSON validity

Decision: `verify-deploy.sh` now takes the live `config.json` path as a second argument, reads the actual `postgres.write`, `postgres.read`, and `redis` connection strings out of it, and attempts a real authenticated connection to each, a `psql SELECT 1` via `docker exec` into the primary and replica containers, and a `redis-cli PING` via `docker exec` into the Redis container. Any auth failure fails the deploy step immediately, `cd.yml`'s "verify deploy" step now also passes `CORE_BE_CONFIG_PATH` through as an env var for this purpose.

Reason: `config_is_valid_json` only ever proved the file parses as JSON, it never proved the credentials inside it actually work against the live datastores, exactly the class of mistake that broke signup this round, a secret and a database drifting out of sync with no automated check to catch it. Testing the real password against the real running container, right after every deploy, is the only way `cd.yml` itself can catch this before a human notices signup is broken.
