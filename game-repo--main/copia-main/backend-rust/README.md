# backend-rust (thin finalizer MVP)

This backend is intentionally minimal:

- One internal endpoint: `POST /v1/finalize`
- HMAC auth + nonce replay protection
- Postgres-backed `chain_jobs` queue
- Finalizer worker that sends `settle_game` / `force_refund`

Client wallets should call `create_game` and `join_game` directly from Unity.

## Responsibilities

1. Trusted game server submits final outcome to `/v1/finalize`.
2. Backend verifies on-chain `Game` account state.
3. Backend upserts minimal `matches` metadata from chain data.
4. Backend enqueues a `chain_jobs` record.
5. Finalizer worker signs and submits settlement/refund using `AUTHORITY_KEYPAIR_PATH`.

## Required env vars

- `APP_BIND_ADDR`
- `DATABASE_URL`
- `SOLANA_RPC_URL`
- `PROGRAM_ID`
- `AUTHORITY_PUBKEY`
- `AUTHORITY_KEYPAIR_PATH`
- `INTERNAL_HMAC_SECRET`
- `FINALIZER_POLL_MS`

## Run locally

1. `cp .env.example .env`
2. Ensure Postgres DB exists and is reachable via `DATABASE_URL`
3. `cargo run`

Startup runs migrations automatically and starts the HTTP server + finalizer worker.
