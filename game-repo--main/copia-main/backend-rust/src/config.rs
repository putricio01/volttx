use anyhow::{Context, Result};

#[derive(Debug, Clone)]
pub struct Config {
    pub app_bind_addr: String,
    pub database_url: String,
    pub solana_rpc_url: String,
    pub program_id: String,
    pub authority_pubkey: String,
    pub authority_keypair_path: String,
    pub internal_hmac_secret: String,
    pub join_timeout_seconds: i64,
    pub settle_timeout_seconds: i64,
    pub finalizer_poll_ms: u64,
    pub timeout_watcher_poll_ms: u64,
}

impl Config {
    pub fn from_env() -> Result<Self> {
        Ok(Self {
            app_bind_addr: env("APP_BIND_ADDR")?,
            database_url: env("DATABASE_URL")?,
            solana_rpc_url: env("SOLANA_RPC_URL")?,
            program_id: env("PROGRAM_ID")?,
            authority_pubkey: env("AUTHORITY_PUBKEY")?,
            authority_keypair_path: env("AUTHORITY_KEYPAIR_PATH")?,
            internal_hmac_secret: env("INTERNAL_HMAC_SECRET")?,
            join_timeout_seconds: env_parse("JOIN_TIMEOUT_SECONDS")?,
            settle_timeout_seconds: env_parse("SETTLE_TIMEOUT_SECONDS")?,
            finalizer_poll_ms: env_parse("FINALIZER_POLL_MS")?,
            timeout_watcher_poll_ms: env_parse("TIMEOUT_WATCHER_POLL_MS")?,
        })
    }
}

fn env(name: &str) -> Result<String> {
    std::env::var(name).with_context(|| format!("missing env var {}", name))
}

fn env_parse<T>(name: &str) -> Result<T>
where
    T: std::str::FromStr,
    T::Err: std::fmt::Display + Send + Sync + 'static,
{
    let raw = env(name)?;
    raw.parse::<T>()
        .with_context(|| format!("invalid value for {}: {}", name, raw))
}
