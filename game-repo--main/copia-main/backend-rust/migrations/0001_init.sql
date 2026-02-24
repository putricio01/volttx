create table if not exists matches (
  match_id bigserial primary key,
  join_code varchar(12) not null unique,

  program_id text not null,
  authority_pubkey text not null,
  game_pda text not null unique,
  vault_pda text not null unique,

  player1_pubkey text not null,
  player2_pubkey text,

  entry_lamports bigint not null check (entry_lamports > 0),

  match_status text not null check (
    match_status in (
      'waiting_create_tx',
      'created_on_chain',
      'joined_on_chain',
      'in_progress',
      'result_pending_finalize',
      'finalizing',
      'settled',
      'refunded'
    )
  ),

  finalization_reason_code text,
  finalization_reason_detail text,
  result_idempotency_key text,
  winner_pubkey text,

  create_tx_sig text,
  join_tx_sig text,
  final_tx_sig text,

  join_expires_at timestamptz,
  settle_expires_at timestamptz,

  created_onchain_at timestamptz,
  joined_onchain_at timestamptz,
  result_reported_at timestamptz,
  finalized_at timestamptz,

  last_error text,

  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create index if not exists idx_matches_status on matches (match_status);
create index if not exists idx_matches_join_code on matches (join_code);
create index if not exists idx_matches_join_expires_at on matches (join_expires_at);
create index if not exists idx_matches_settle_expires_at on matches (settle_expires_at);
create index if not exists idx_matches_player1 on matches (player1_pubkey);
create index if not exists idx_matches_player2 on matches (player2_pubkey);

create table if not exists chain_jobs (
  id bigserial primary key,
  match_id bigint not null unique references matches(match_id) on delete cascade,

  job_type text not null check (job_type in ('settle', 'force_refund')),
  status text not null check (status in ('pending', 'submitted', 'retrying', 'confirmed', 'failed')),

  winner_pubkey text,
  attempt_count integer not null default 0,
  next_attempt_at timestamptz not null default now(),

  last_tx_sig text,
  last_error text,

  lock_token uuid,
  locked_at timestamptz,

  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),

  check (
    (job_type = 'settle' and winner_pubkey is not null)
    or
    (job_type = 'force_refund')
  )
);

create index if not exists idx_chain_jobs_due on chain_jobs (status, next_attempt_at);

create table if not exists used_nonces (
  nonce text primary key,
  created_at timestamptz not null default now()
);

create index if not exists idx_used_nonces_created_at on used_nonces (created_at);

