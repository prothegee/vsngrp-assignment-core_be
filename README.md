# Core BE

Core BE is the account service for the chat-bot product. It owns account signup, signin, signout, and session refresh over a REST API. It issues the JWT that the other services trust and never talks to the chat model directly, that is Core BE WS's job.

<br>

## What it does

- `POST /auth/signup`: create an account with an email and a password.
- `POST /auth/signin`: verify credentials, start a session, return an access token.
- `POST /auth/signout`: end the current session immediately.
- `POST /auth/refresh`: exchange a valid refresh token cookie for a new access token, rotates the refresh token.
- `GET /health`: status, version, and the exact commit deployed.

Accounts live in PostgreSQL. Sessions live in Redis. Access tokens are short-lived JWTs (HS256), refresh tokens are long-lived opaque values stored in an httpOnly cookie.

<br>

## Prerequisites

- .NET 10 SDK
- Docker (or a Docker-compatible engine, for example Podman)

<br>

## Setup

1. Copy the config template and fill in real values:
   ```
   cp config/config.json.template config/config.json
   ```
2. Apply database migrations (starts the datastores automatically if they are not already running):
   ```
   ./containers.sh up
   dotnet ef database update --project src
   ```
3. Run the service:
   ```
   ./debug.sh
   ```
   `debug.sh` warns and copies the config template for you if `config/config.json` is missing, checks the datastore containers and starts them if they are not already running, then starts the service.

The service listens on the port set in `config.json` (`9001` by default). See `development.md` for a full local setup walkthrough and `deployment.md` for production setup.

<br>

## Managing the datastores

```
./containers.sh {up|down|start|stop|cleanup}
```

`up` and `down` create/destroy the containers, `start`/`stop` pause and resume the existing ones without losing data, `cleanup` permanently wipes the Postgres and Redis data directories, it asks for a typed `YES` confirmation first since it cannot be undone.

<br>

## Testing

```
./run-tests.sh
```

This runs everything: the full unit, edge, and integration suite (`dotnet test`), then the database integrity runner (`run-tests-db-integrity.sh`). Both stages spin up their own throwaway containers and clean up after themselves, no manual setup needed.

The database integrity runner can also be run on its own:

```
./run-tests-db-integrity.sh
```

It is a non-.NET sanity check of the database layer itself (schema, unique constraints, Redis read/write/ttl), against a pair of default, unmodified Postgres and Redis containers unrelated to the `containers/` deployment setup. It starts its own containers, runs its checks, then removes those containers and their images again.

<br>

## More documentation

- `hld.md`: how the service fits into the wider system.
- `lld.md`: project structure, data schema, and internal design.
- `adr.md`: why specific technical choices were made.
- `development.md`: local development guide.
- `deployment.md`: production deployment guide.
