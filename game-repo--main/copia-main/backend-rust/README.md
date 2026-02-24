# backend-rust (MVP scaffold)

This folder is a **Rust backend scaffold** for your 1v1 Solana-bet match flow.

It includes:
- `axum` HTTP route skeletons for the 7 MVP endpoints
- `sqlx` Postgres connection + startup migrations
- Postgres migration file with `matches`, `chain_jobs`, `used_nonces`
- Worker task stubs (`finalizer`, `timeout_watcher`)

It does **not** yet include:
- real Solana RPC calls
- PDA derivation
- Anchor account decoding
- HMAC verification logic
- actual DB query implementations

## What is `axum`?

`axum` is a Rust web framework (similar role to `Express` in Node or `FastAPI` in Python).

You use it to define routes like:
- `POST /v1/matches`
- `GET /v1/matches/{id}/status`

In this scaffold, route handlers live in:
- `src/api/matches.rs`
- `src/api/admin.rs`

## What is `sqlx`?

`sqlx` is a Rust database library (similar role to a DB client/ORM layer).

You use it to:
- connect to Postgres (`PgPool`)
- run SQL queries
- run migrations

In this scaffold:
- startup opens a `PgPool`
- `sqlx::migrate!("./migrations")` runs SQL files in `migrations/`

## Why this structure?

It matches the MVP responsibilities you defined:
- API routes (`axum`) for Unity and internal result/admin calls
- DB persistence (`sqlx`) so matches survive restarts
- background workers for retries/timeouts
- Solana module placeholders for settlement/refund integration

## Run locally (after you add Postgres)

1. Copy env file
   - `cp .env.example .env`
2. Start Postgres and create the DB `volttx_mvp`
3. Run the backend
   - `cargo run`

On startup it will:
- load `.env`
- connect to Postgres
- run migrations
- start HTTP server
- start worker stubs

## Next implementation steps (in order)

1. Implement `POST /v1/matches` DB insert + join code generation
2. Add Solana PDA derivation helper (server-side expected `game_pda`/`vault_pda`)
3. Implement `create-confirm` / `join-confirm` on-chain verification
4. Implement `/result` -> create `chain_jobs` row
5. Implement finalizer worker (`settle_game` / `force_refund`)
6. Implement timeout watcher (`join_timeout -> force_refund`)

