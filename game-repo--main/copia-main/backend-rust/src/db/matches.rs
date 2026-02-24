//! DB helpers for `matches`.

use chrono::{DateTime, Utc};
use sqlx::{PgPool, Row};

use crate::error::AppError;
use crate::models::enums::{ChainJobStatus, ChainJobType, MatchStatus};

#[derive(Debug, Clone)]
pub struct CreateMatchRecordParams<'a> {
    pub program_id: &'a str,
    pub authority_pubkey: &'a str,
    pub player1_pubkey: &'a str,
    pub entry_lamports: i64,
    pub game_pda: &'a str,
    pub vault_pda: &'a str,
}

#[derive(Debug, Clone)]
pub struct CreatedMatchRecord {
    pub match_id: i64,
    pub join_code: String,
}

#[derive(Debug, Clone)]
pub struct MatchLookupRecord {
    pub match_id: i64,
    pub join_code: String,
    pub game_pda: String,
    pub vault_pda: String,
    pub player1_pubkey: String,
    pub entry_lamports: i64,
    pub match_status: MatchStatus,
    pub join_expires_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone)]
pub struct CreateConfirmMatchRecord {
    pub match_id: i64,
    pub player1_pubkey: String,
    pub authority_pubkey: String,
    pub game_pda: String,
    pub entry_lamports: i64,
    pub match_status: MatchStatus,
    pub create_tx_sig: Option<String>,
    pub join_expires_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone)]
pub struct CreateConfirmUpdateResult {
    pub match_id: i64,
    pub match_status: MatchStatus,
    pub create_tx_sig: Option<String>,
    pub join_expires_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone)]
pub struct JoinConfirmMatchRecord {
    pub match_id: i64,
    pub player1_pubkey: String,
    pub player2_pubkey: Option<String>,
    pub authority_pubkey: String,
    pub game_pda: String,
    pub entry_lamports: i64,
    pub match_status: MatchStatus,
    pub join_tx_sig: Option<String>,
    pub settle_expires_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone)]
pub struct JoinConfirmUpdateResult {
    pub match_id: i64,
    pub player2_pubkey: Option<String>,
    pub match_status: MatchStatus,
    pub join_tx_sig: Option<String>,
    pub settle_expires_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone)]
pub struct MatchStatusRecord {
    pub match_id: i64,
    pub join_code: String,
    pub program_id: String,
    pub authority_pubkey: String,
    pub game_pda: String,
    pub vault_pda: String,
    pub player1_pubkey: String,
    pub player2_pubkey: Option<String>,
    pub entry_lamports: i64,
    pub match_status: MatchStatus,
    pub chain_job_type: Option<ChainJobType>,
    pub chain_job_status: Option<ChainJobStatus>,
    pub winner_pubkey: Option<String>,
    pub finalization_reason_code: Option<String>,
    pub create_tx_sig: Option<String>,
    pub join_tx_sig: Option<String>,
    pub final_tx_sig: Option<String>,
    pub join_expires_at: Option<DateTime<Utc>>,
    pub settle_expires_at: Option<DateTime<Utc>>,
    pub last_error: Option<String>,
    pub updated_at: DateTime<Utc>,
}

pub async fn reserve_next_match_id(pool: &PgPool) -> Result<i64, AppError> {
    let match_id: i64 =
        sqlx::query_scalar("select nextval(pg_get_serial_sequence('matches', 'match_id'))")
            .fetch_one(pool)
            .await
            .map_err(|e| AppError::Internal(format!("failed to reserve match_id: {e}")))?;

    Ok(match_id)
}

pub async fn insert_create_match_record(
    pool: &PgPool,
    match_id: i64,
    join_code: &str,
    params: &CreateMatchRecordParams<'_>,
) -> Result<CreatedMatchRecord, AppError> {
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
          entry_lamports,
          match_status
        )
        values ($1, $2, $3, $4, $5, $6, $7, $8, 'waiting_create_tx')
        returning match_id, join_code
        "#,
    )
    .bind(match_id)
    .bind(join_code)
    .bind(params.program_id)
    .bind(params.authority_pubkey)
    .bind(params.game_pda)
    .bind(params.vault_pda)
    .bind(params.player1_pubkey)
    .bind(params.entry_lamports)
    .fetch_one(pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to insert match record: {e}")))?;

    Ok(CreatedMatchRecord {
        match_id: row.get::<i64, _>("match_id"),
        join_code: row.get::<String, _>("join_code"),
    })
}

pub async fn get_match_lookup_by_join_code(
    pool: &PgPool,
    join_code: &str,
) -> Result<Option<MatchLookupRecord>, AppError> {
    let row = sqlx::query(
        r#"
        select
          match_id,
          join_code,
          game_pda,
          vault_pda,
          player1_pubkey,
          entry_lamports,
          match_status,
          join_expires_at
        from matches
        where join_code = $1
        "#,
    )
    .bind(join_code)
    .fetch_optional(pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to load match by join code: {e}")))?;

    row.map(map_match_lookup_row).transpose()
}

pub async fn get_match_for_create_confirm(
    pool: &PgPool,
    match_id: i64,
) -> Result<Option<CreateConfirmMatchRecord>, AppError> {
    let row = sqlx::query(
        r#"
        select
          match_id,
          player1_pubkey,
          authority_pubkey,
          game_pda,
          entry_lamports,
          match_status,
          create_tx_sig,
          join_expires_at
        from matches
        where match_id = $1
        "#,
    )
    .bind(match_id)
    .fetch_optional(pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to load match for create-confirm: {e}")))?;

    row.map(map_create_confirm_row).transpose()
}

pub async fn mark_match_created_on_chain(
    pool: &PgPool,
    match_id: i64,
    create_tx_sig: &str,
    created_onchain_at: DateTime<Utc>,
    join_expires_at: DateTime<Utc>,
) -> Result<CreateConfirmUpdateResult, AppError> {
    let row = sqlx::query(
        r#"
        update matches
        set
          match_status = case
            when match_status = 'waiting_create_tx' then 'created_on_chain'
            else match_status
          end,
          create_tx_sig = coalesce(create_tx_sig, $2),
          created_onchain_at = coalesce(created_onchain_at, $3),
          join_expires_at = coalesce(join_expires_at, $4),
          updated_at = now()
        where match_id = $1
        returning match_id, match_status, create_tx_sig, join_expires_at
        "#,
    )
    .bind(match_id)
    .bind(create_tx_sig)
    .bind(created_onchain_at)
    .bind(join_expires_at)
    .fetch_one(pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to update create-confirm state: {e}")))?;

    map_create_confirm_update_row(row)
}

pub async fn get_match_for_join_confirm(
    pool: &PgPool,
    match_id: i64,
) -> Result<Option<JoinConfirmMatchRecord>, AppError> {
    let row = sqlx::query(
        r#"
        select
          match_id,
          player1_pubkey,
          player2_pubkey,
          authority_pubkey,
          game_pda,
          entry_lamports,
          match_status,
          join_tx_sig,
          settle_expires_at
        from matches
        where match_id = $1
        "#,
    )
    .bind(match_id)
    .fetch_optional(pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to load match for join-confirm: {e}")))?;

    row.map(map_join_confirm_row).transpose()
}

pub async fn mark_match_joined_on_chain(
    pool: &PgPool,
    match_id: i64,
    player2_pubkey: &str,
    join_tx_sig: &str,
    joined_onchain_at: DateTime<Utc>,
    settle_expires_at: DateTime<Utc>,
) -> Result<JoinConfirmUpdateResult, AppError> {
    let row = sqlx::query(
        r#"
        update matches
        set
          match_status = case
            when match_status = 'created_on_chain' then 'joined_on_chain'
            else match_status
          end,
          player2_pubkey = coalesce(player2_pubkey, $2),
          join_tx_sig = coalesce(join_tx_sig, $3),
          joined_onchain_at = coalesce(joined_onchain_at, $4),
          settle_expires_at = coalesce(settle_expires_at, $5),
          updated_at = now()
        where match_id = $1
          and (player2_pubkey is null or player2_pubkey = $2)
        returning match_id, player2_pubkey, match_status, join_tx_sig, settle_expires_at
        "#,
    )
    .bind(match_id)
    .bind(player2_pubkey)
    .bind(join_tx_sig)
    .bind(joined_onchain_at)
    .bind(settle_expires_at)
    .fetch_optional(pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to update join-confirm state: {e}")))?;

    let row = row.ok_or_else(|| {
        AppError::Conflict("join-confirm update rejected due to player2 mismatch".into())
    })?;

    map_join_confirm_update_row(row)
}

pub async fn get_match_status_record(
    pool: &PgPool,
    match_id: i64,
) -> Result<Option<MatchStatusRecord>, AppError> {
    let row = sqlx::query(
        r#"
        select
          m.match_id,
          m.join_code,
          m.program_id,
          m.authority_pubkey,
          m.game_pda,
          m.vault_pda,
          m.player1_pubkey,
          m.player2_pubkey,
          m.entry_lamports,
          m.match_status,
          cj.job_type as chain_job_type,
          cj.status as chain_job_status,
          m.winner_pubkey,
          m.finalization_reason_code,
          m.create_tx_sig,
          m.join_tx_sig,
          coalesce(m.final_tx_sig, cj.last_tx_sig) as final_tx_sig,
          m.join_expires_at,
          m.settle_expires_at,
          coalesce(cj.last_error, m.last_error) as last_error,
          greatest(m.updated_at, coalesce(cj.updated_at, m.updated_at)) as updated_at
        from matches m
        left join chain_jobs cj on cj.match_id = m.match_id
        where m.match_id = $1
        "#,
    )
    .bind(match_id)
    .fetch_optional(pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to load match status: {e}")))?;

    row.map(map_match_status_row).transpose()
}

pub fn join_code_from_match_id(match_id: i64) -> Result<String, AppError> {
    if match_id <= 0 {
        return Err(AppError::Internal(format!(
            "match_id must be positive, got {match_id}"
        )));
    }

    // Deterministic base36 code keeps MVP logic simple and collision-free.
    let code = to_base36(match_id as u64);
    Ok(format!("M{code}"))
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

fn map_match_lookup_row(row: sqlx::postgres::PgRow) -> Result<MatchLookupRecord, AppError> {
    Ok(MatchLookupRecord {
        match_id: row.get::<i64, _>("match_id"),
        join_code: row.get::<String, _>("join_code"),
        game_pda: row.get::<String, _>("game_pda"),
        vault_pda: row.get::<String, _>("vault_pda"),
        player1_pubkey: row.get::<String, _>("player1_pubkey"),
        entry_lamports: row.get::<i64, _>("entry_lamports"),
        match_status: parse_match_status(row.get::<String, _>("match_status").as_str())?,
        join_expires_at: row.get::<Option<DateTime<Utc>>, _>("join_expires_at"),
    })
}

fn map_create_confirm_row(
    row: sqlx::postgres::PgRow,
) -> Result<CreateConfirmMatchRecord, AppError> {
    Ok(CreateConfirmMatchRecord {
        match_id: row.get::<i64, _>("match_id"),
        player1_pubkey: row.get::<String, _>("player1_pubkey"),
        authority_pubkey: row.get::<String, _>("authority_pubkey"),
        game_pda: row.get::<String, _>("game_pda"),
        entry_lamports: row.get::<i64, _>("entry_lamports"),
        match_status: parse_match_status(row.get::<String, _>("match_status").as_str())?,
        create_tx_sig: row.get::<Option<String>, _>("create_tx_sig"),
        join_expires_at: row.get::<Option<DateTime<Utc>>, _>("join_expires_at"),
    })
}

fn map_create_confirm_update_row(
    row: sqlx::postgres::PgRow,
) -> Result<CreateConfirmUpdateResult, AppError> {
    Ok(CreateConfirmUpdateResult {
        match_id: row.get::<i64, _>("match_id"),
        match_status: parse_match_status(row.get::<String, _>("match_status").as_str())?,
        create_tx_sig: row.get::<Option<String>, _>("create_tx_sig"),
        join_expires_at: row.get::<Option<DateTime<Utc>>, _>("join_expires_at"),
    })
}

fn map_join_confirm_row(row: sqlx::postgres::PgRow) -> Result<JoinConfirmMatchRecord, AppError> {
    Ok(JoinConfirmMatchRecord {
        match_id: row.get::<i64, _>("match_id"),
        player1_pubkey: row.get::<String, _>("player1_pubkey"),
        player2_pubkey: row.get::<Option<String>, _>("player2_pubkey"),
        authority_pubkey: row.get::<String, _>("authority_pubkey"),
        game_pda: row.get::<String, _>("game_pda"),
        entry_lamports: row.get::<i64, _>("entry_lamports"),
        match_status: parse_match_status(row.get::<String, _>("match_status").as_str())?,
        join_tx_sig: row.get::<Option<String>, _>("join_tx_sig"),
        settle_expires_at: row.get::<Option<DateTime<Utc>>, _>("settle_expires_at"),
    })
}

fn map_join_confirm_update_row(
    row: sqlx::postgres::PgRow,
) -> Result<JoinConfirmUpdateResult, AppError> {
    Ok(JoinConfirmUpdateResult {
        match_id: row.get::<i64, _>("match_id"),
        player2_pubkey: row.get::<Option<String>, _>("player2_pubkey"),
        match_status: parse_match_status(row.get::<String, _>("match_status").as_str())?,
        join_tx_sig: row.get::<Option<String>, _>("join_tx_sig"),
        settle_expires_at: row.get::<Option<DateTime<Utc>>, _>("settle_expires_at"),
    })
}

fn map_match_status_row(row: sqlx::postgres::PgRow) -> Result<MatchStatusRecord, AppError> {
    let chain_job_type_raw = row.get::<Option<String>, _>("chain_job_type");
    let chain_job_status_raw = row.get::<Option<String>, _>("chain_job_status");

    Ok(MatchStatusRecord {
        match_id: row.get::<i64, _>("match_id"),
        join_code: row.get::<String, _>("join_code"),
        program_id: row.get::<String, _>("program_id"),
        authority_pubkey: row.get::<String, _>("authority_pubkey"),
        game_pda: row.get::<String, _>("game_pda"),
        vault_pda: row.get::<String, _>("vault_pda"),
        player1_pubkey: row.get::<String, _>("player1_pubkey"),
        player2_pubkey: row.get::<Option<String>, _>("player2_pubkey"),
        entry_lamports: row.get::<i64, _>("entry_lamports"),
        match_status: parse_match_status(row.get::<String, _>("match_status").as_str())?,
        chain_job_type: parse_chain_job_type_opt(chain_job_type_raw.as_deref())?,
        chain_job_status: parse_chain_job_status_opt(chain_job_status_raw.as_deref())?,
        winner_pubkey: row.get::<Option<String>, _>("winner_pubkey"),
        finalization_reason_code: row.get::<Option<String>, _>("finalization_reason_code"),
        create_tx_sig: row.get::<Option<String>, _>("create_tx_sig"),
        join_tx_sig: row.get::<Option<String>, _>("join_tx_sig"),
        final_tx_sig: row.get::<Option<String>, _>("final_tx_sig"),
        join_expires_at: row.get::<Option<DateTime<Utc>>, _>("join_expires_at"),
        settle_expires_at: row.get::<Option<DateTime<Utc>>, _>("settle_expires_at"),
        last_error: row.get::<Option<String>, _>("last_error"),
        updated_at: row.get::<DateTime<Utc>, _>("updated_at"),
    })
}

fn parse_match_status(raw: &str) -> Result<MatchStatus, AppError> {
    let status = match raw {
        "waiting_create_tx" => MatchStatus::WaitingCreateTx,
        "created_on_chain" => MatchStatus::CreatedOnChain,
        "joined_on_chain" => MatchStatus::JoinedOnChain,
        "in_progress" => MatchStatus::InProgress,
        "result_pending_finalize" => MatchStatus::ResultPendingFinalize,
        "finalizing" => MatchStatus::Finalizing,
        "settled" => MatchStatus::Settled,
        "refunded" => MatchStatus::Refunded,
        _ => {
            return Err(AppError::Internal(format!(
                "unknown match_status in DB: {raw}"
            )))
        }
    };
    Ok(status)
}

fn parse_chain_job_type_opt(raw: Option<&str>) -> Result<Option<ChainJobType>, AppError> {
    let Some(raw) = raw else { return Ok(None) };
    let parsed = match raw {
        "settle" => ChainJobType::Settle,
        "force_refund" => ChainJobType::ForceRefund,
        _ => {
            return Err(AppError::Internal(format!(
                "unknown chain_jobs.job_type in DB: {raw}"
            )))
        }
    };
    Ok(Some(parsed))
}

fn parse_chain_job_status_opt(raw: Option<&str>) -> Result<Option<ChainJobStatus>, AppError> {
    let Some(raw) = raw else { return Ok(None) };
    let parsed = match raw {
        "pending" => ChainJobStatus::Pending,
        "submitted" => ChainJobStatus::Submitted,
        "retrying" => ChainJobStatus::Retrying,
        "confirmed" => ChainJobStatus::Confirmed,
        "failed" => ChainJobStatus::Failed,
        _ => {
            return Err(AppError::Internal(format!(
                "unknown chain_jobs.status in DB: {raw}"
            )))
        }
    };
    Ok(Some(parsed))
}
