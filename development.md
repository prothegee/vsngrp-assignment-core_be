# Development Guide: Core BE

<br>

## Prerequisites

- .NET 10 SDK
- Docker (or a Docker-compatible engine, for example Podman)

<br>

## First-time setup

1. Restore local dotnet tools (this installs `dotnet-ef` as a project-local tool, from `dotnet-tools.json`, no global install or PATH setup needed):
   ```
   dotnet tool restore
   ```

2. Copy the config template:
   ```
   cp config/config.json.template config/config.json
   ```
   Fill in a `jwtSecret` (any non-empty string for local development), and leave the connection strings pointing at `localhost` matching the ports below.

3. Create the bind-mounted data directories if they are not already writable by the container engine's user:
   ```
   chmod 777 containers/postgres-primary/data containers/postgres-replica/data containers/redis/data
   ```
   This is only needed once. It matters most on rootless Podman, where the container's internal user id does not map to the host user that owns the directory.

4. Start the datastores:
   ```
   ./containers.sh up
   ```
   This starts a Postgres primary on `5432`, a Postgres streaming replica on `5433`, and Redis on `6379`, all bound to `127.0.0.1`.

5. Apply migrations against the primary:
   ```
   export MIGRATIONS_CONNECTION_STRING="Host=localhost;Port=5432;Database=vsngrp_core_be;Username=core_be;Password=devpassword"
   dotnet ef database update --project src
   ```

<br>

## Managing the datastores

```
./containers.sh {up|down|start|stop|cleanup}
```

| Command | Effect |
| :- |
| `up` | create and start the containers |
| `down` | stop and remove the containers, data directories are left alone |
| `start` | start the existing containers back up without recreating them |
| `stop` | stop the running containers without removing them |
| `cleanup` | permanently wipe the Postgres and Redis data directories, prints an all-caps warning and refuses to proceed unless typed exactly `YES` |

`cleanup` also runs `down` first, and wipes the data directories from inside a throwaway container rather than a plain `rm`, since Postgres and Redis create some files under their own container user id, which the host user cannot always delete directly, even with permissive directory permissions.

<br>

## Running the service

```
./debug.sh
```

`debug.sh` checks that `config/config.json` exists first. If it is missing, it copies `config/config.json.template` in its place and prints a warning, since the copied file still has `CHANGE_THIS` placeholders that will not actually authenticate against anything, it only prevents an immediate crash on a fresh checkout. It then checks whether the datastore containers are running, and starts them (`./containers.sh up`) if they are not. It then runs the service the same way `dotnet run` would.

The service reads `config/config.json` (one directory above `src`) and listens on the configured `port` (`9001` by default). Set the `CONFIG_PATH` environment variable to point somewhere else if needed.

<br>

## Running tests

```
./run-tests.sh
```

This runs two stages, in order:

1. `dotnet test`, the full unit, edge, and integration suite. Integration tests use Testcontainers to start their own throwaway Postgres and Redis containers, they do not touch the containers started by `docker compose`.
2. `run-tests-db-integrity.sh`, see below.

If using rootless Podman instead of Docker Engine, point both stages at the Podman API socket first:

```
systemctl --user start podman.socket
export DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock
./run-tests.sh
```

<br>

## Checking database integrity directly

```
./run-tests-db-integrity.sh
```

This normally runs as the last stage of `run-tests.sh`, it can also be run on its own. It is separate from the .NET test project and from the `containers/` deployment setup. It restores `dotnet-ef` from the local tool manifest itself, starts a plain, default Postgres and a plain, default Redis container of its own, applies migrations, checks the `accounts` schema and its unique constraint, checks Redis read, write, and TTL behavior, then removes those containers and their images again. It needs the same `DOCKER_HOST` setup as above if using rootless Podman.

<br>

## Creating a new migration

```
cd src
export MIGRATIONS_CONNECTION_STRING="Host=localhost;Port=5432;Database=vsngrp_core_be;Username=core_be;Password=devpassword"
dotnet ef migrations add <Name> -o Data/Migrations
```

<br>

## Formatting

```
dotnet format
```

`ci.yml` runs `dotnet format --verify-no-changes`, run it locally before opening a pull request.

<br>

## Troubleshooting

- **Redis container exits immediately with a permission error**: the bind-mounted data directory is not writable by the container's user. See step 2 above.
- **`config.json failed to bind to AppConfig`**: `config/config.json` is missing or not valid JSON. Re-copy it from `config/config.json.template`.
- **Signin works but the accounts table looks empty on the replica right after signup**: streaming replication has a small lag, usually well under a second. Retry the read.
- **`error MSB4242: SDK Resolver Failure` mentioning a workload set version**: this is a broken or incomplete .NET workload manifest install on the machine, unrelated to this project (this project has no workload dependencies at all, no MAUI, no mobile, no wasm-tools). `Directory.Build.props` at the repository root already sets `MSBuildEnableWorkloadResolver` to `false` for exactly this reason, so this should not happen. If it does, confirm `Directory.Build.props` exists at the repository root and was not accidentally removed.
- **`Could not execute because the specified command or file was not found` when a script runs `dotnet ef`**: `dotnet-ef` was never restored as a local tool. Run `dotnet tool restore` from the repository root once.
