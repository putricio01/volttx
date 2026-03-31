-- Server pool: tracks dedicated game server instances
create table if not exists server_pool (
  server_id text primary key,
  ip text not null,
  port integer not null,
  status text not null default 'idle' check (status in ('idle', 'busy', 'offline')),
  assigned_match_id bigint references matches(match_id),
  last_heartbeat_at timestamptz not null default now(),
  created_at timestamptz not null default now()
);

create index if not exists idx_server_pool_status on server_pool (status);

-- Extend matches: track which server is assigned and the acceptor's pubkey
alter table matches add column if not exists assigned_server_id text references server_pool(server_id);
alter table matches add column if not exists acceptor_pubkey text;
