# High-Level Design: Core BE

<br>

## System context

Core BE is one of three services in the chat-bot product:

```mermaid
flowchart LR
    FE["Core FE : port 9003
    browser client"]
    BE["Core BE : port 9001
    accounts and sessions"]
    BEWS["Core BE WS : port 9002
    chat over WebSocket"]
    PGPrimary[("PostgreSQL 18 primary
    accounts, writes")]
    PGReplica[("PostgreSQL 18 replica
    accounts, reads")]
    Redis[("Redis 8
    sessions")]

    FE -- "REST: signup / signin / signout / refresh" --> BE
    FE -- "WebSocket: auth-frame + chat" --> BEWS
    BE -- "writes" --> PGPrimary
    BE -- "reads" --> PGReplica
    PGPrimary -- "streaming replication" --> PGReplica
    BE --> Redis
    BEWS -- "verify session" --> Redis
```

Core BE owns account creation and session lifecycle. It is the only service allowed to write to the accounts table and the only service that issues JWTs. Core BE WS trusts those JWTs by checking the signature against a secret shared between the two services, and confirms the session is still active by reading the same Redis instance Core BE writes to.

<br>

## Responsibilities

- Create accounts with a hashed password, no email verification or OTP.
- Verify credentials on signin and start a session.
- Issue short-lived access tokens (JWT, 15 minutes).
- Track sessions in Redis so signout revokes access immediately, instead of waiting for the token to expire.
- Rotate refresh tokens on every use so a leaked refresh token stops working after its next legitimate use.
- Report its own health and the exact commit running, for both local debugging and deployment verification.

<br>

## What it does not do

- It does not talk to the DeepSeek chat model, that is Core BE WS.
- It does not serve any frontend assets, that is Core FE.
- It does not validate email format or send verification emails, out of scope per spec.

<br>

## Request flow: signin then a protected call

```mermaid
sequenceDiagram
    participant Client
    participant CoreBE as Core BE
    participant Postgres
    participant Redis

    Client->>CoreBE: POST /auth/signin
    CoreBE->>Postgres: read account by email (replica)
    Postgres-->>CoreBE: account row
    CoreBE->>Redis: create session, issue refresh token
    CoreBE-->>Client: access token + refresh token cookie

    Client->>CoreBE: POST /auth/signout (Bearer access token)
    CoreBE->>Redis: check session is still active
    Redis-->>CoreBE: active
    CoreBE->>Redis: delete session
    CoreBE-->>Client: 204
```

<br>

## Dependencies

- PostgreSQL 18: one primary and one streaming replica. Writes (signup) go to the primary, reads (signin lookup) go to the replica.
- Redis 8: session records (`sid -> accountId`) and refresh token records, both with a TTL matching the refresh token lifetime.
- Shared JWT secret with Core BE WS: Core BE issues, Core BE WS only verifies.
