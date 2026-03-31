//! Minimal DB helpers for `matches` in thin-finalizer mode.

use chrono::{DateTime, Utc};
use sqlx::PgPool;

use crate::{error::AppError, models::enums::MatchStatus};

#[derive(Debug, Clone)]
pub struct UpsertMatchFromChainParams<'a> {
    pub match_id: i64,
    pub program_id: &'a str,
    pub authority_pubkey: &'a str,
    pub game_pda: &'a str,
    pub vault_pda: &'a str,
    pub player1_pubkey: &'a str,
    pub player2_pubkey: Option<&'a str>,
    pub entry_lamports: i64,
    pub match_status: MatchStatus,
    pub created_onchain_at: DateTime<Utc>,
    pub joined_onchain_at: Option<DateTime<Utc>>,
}

pub async fn upsert_match_from_chain(
    pool: &PgPool,
    params: &UpsertMatchFromChainParams<'_>,
) -> Result<(), AppError> {
    if params.match_id <= 0 {
        return Err(AppError::BadRequest("match_id must be positive".into()));
    }
    if params.entry_lamports <= 0 {
        return Err(AppError::BadRequest("entry_lamports must be > 0".into()));
    }

    let join_code = join_code_from_match_id(params.match_id)?;
    let match_status_db = match_status_to_seed_db(params.match_status)?;

    let row = sqlx::query(
        r#"
        insert into matches (
          match_id,
          join_code,
          program_id,
          authority_pubkey,
          game_pda,
          vault_pda,
          player1_pubkey,
          player2_pubkey,
          entry_lamports,
          match_status,
          created_onchain_at,
          joined_onchain_at
        )
        values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)
        on conflict (match_id) do update
        set
          program_id = excluded.program_id,
          authority_pubkey = excluded.authority_pubkey,
          game_pda = excluded.game_pda,
          vault_pda = excluded.vault_pda,
          player1_pubkey = excluded.player1_pubkey,
          player2_pubkey = coalesce(matches.player2_pubkey, excluded.player2_pubkey),
          entry_lamports = excluded.entry_lamports,
          match_status = case
            when matches.match_status in ('settled', 'refunded', 'result_pending_finalize', 'finalizing')
              then matches.match_status
            when matches.match_status = 'joined_on_chain'
              then 'joined_on_chain'
            when matches.match_status = 'in_progress'
              then 'in_progress'
            when matches.match_status = 'created_on_chain' and excluded.match_status = 'joined_on_chain'
              then 'joined_on_chain'
            else excluded.match_status
          end,
          created_onchain_at = coalesce(matches.created_onchain_at, excluded.created_onchain_at),
          joined_onchain_at = coalesce(matches.joined_onchain_at, excluded.joined_onchain_at),
          updated_at = now()
        where matches.program_id = excluded.program_id
          and matches.authority_pubkey = excluded.authority_pubkey
          and matches.game_pda = excluded.game_pda
          and matches.vault_pda = excluded.vault_pda
          and matches.player1_pubkey = excluded.player1_pubkey
          and matches.entry_lamports = excluded.entry_lamports
          and (
            matches.player2_pubkey is null
            or excluded.player2_pubkey is null
            or matches.player2_pubkey = excluded.player2_pubkey
          )
        returning match_id
        "#,
    )
    .bind(params.match_id)
    .bind(join_code)
    .bind(params.program_id)
    .bind(params.authority_pubkey)
    .bind(params.game_pda)
    .bind(params.vault_pda)
    .bind(params.player1_pubkey)
    .bind(params.player2_pubkey)
    .bind(params.entry_lamports)
    .bind(match_status_db)
    .bind(params.created_onchain_at)
    .bind(params.joined_onchain_at)
    .fetch_optional(pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to upsert match from chain: {e}")))?;

    if row.is_none() {
        return Err(AppError::Conflict(
            "existing match row conflicts with on-chain match metadata".into(),
        ));
    }

    Ok(())
}

pub fn join_code_from_match_id(match_id: i64) -> Result<String, AppError> {
    if match_id <= 0 {
        return Err(AppError::Internal(format!(
            "match_id must be positive, got {match_id}"
        )));
    }

    // Deterministic base36 code keeps compatibility with existing DB schema constraints.
    let code = to_base36(match_id as u64);
    Ok(format!("M{code}"))
}

fn match_status_to_seed_db(value: MatchStatus) -> Result<&'static str, AppError> {
    match value {
        MatchStatus::CreatedOnChain => Ok("created_on_chain"),
        MatchStatus::JoinedOnChain => Ok("joined_on_chain"),
        MatchStatus::InProgress => Ok("in_progress"),
        _ => Err(AppError::Internal(format!(
            "invalid seed status for match upsert: {:?}",
            value
        ))),
    }
}

fn to_base36(mut value: u64) -> String {
    const DIGITS: &[u8; 36] = b"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    if value == 0 {
        return "0".to_string();
    }

    let mut buf = [0u8; 32];
    let mut idx = buf.len();
    while value > 0 {
        let rem = (value % 36) as usize;
        idx -= 1;
        buf[idx] = DIGITS[rem];
        value /= 36;
    }
    String::from_utf8_lossy(&buf[idx..]).into_owned()
}
