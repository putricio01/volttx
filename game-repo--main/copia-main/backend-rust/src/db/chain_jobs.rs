//! DB helpers for `chain_jobs`.
//!
//! This module will own:
//! - enqueue finalization job
//! - lock next due job (`FOR UPDATE SKIP LOCKED`)
//! - mark submitted/retrying/confirmed/failed

use chrono::Utc;
use sqlx::{PgPool, Row};
use uuid::Uuid;

use crate::{
    error::AppError,
    models::enums::{ChainJobStatus, ChainJobType, MatchStatus},
};

#[derive(Debug, Clone)]
pub struct PersistResultAndEnqueueParams {
    pub match_id: i64,
    pub job_type: ChainJobType,
    pub winner_pubkey: Option<String>,
    pub reason_code: String,
    pub reason_detail: Option<String>,
    pub idempotency_key: String,
}

#[derive(Debug, Clone)]
pub struct PersistResultAndEnqueueResult {
    pub match_status: MatchStatus,
    pub chain_job_type: ChainJobType,
    pub chain_job_status: ChainJobStatus,
}

#[derive(Debug, Clone)]
pub struct ClaimedFinalizerJob {
    pub chain_job_id: i64,
    pub match_id: i64,
    pub lock_token: Uuid,
    pub job_type: ChainJobType,
    pub chain_job_status: ChainJobStatus,
    pub winner_pubkey: Option<String>,
    pub attempt_count: i32,
    pub last_tx_sig: Option<String>,
    pub game_pda: String,
    pub vault_pda: String,
}

#[derive(Debug)]
struct MatchResultUpdateRow {
    match_status: MatchStatus,
}

#[derive(Debug)]
struct ChainJobUpsertRow {
    job_type: ChainJobType,
    status: ChainJobStatus,
}

#[derive(Debug)]
struct ClaimedJobSelectRow {
    chain_job_id: i64,
    match_id: i64,
    job_type: ChainJobType,
    chain_job_status: ChainJobStatus,
    winner_pubkey: Option<String>,
    attempt_count: i32,
    last_tx_sig: Option<String>,
    game_pda: String,
    vault_pda: String,
}

pub async fn persist_result_and_enqueue(
    pool: &PgPool,
    params: PersistResultAndEnqueueParams,
) -> Result<PersistResultAndEnqueueResult, AppError> {
    let mut tx = pool
        .begin()
        .await
        .map_err(|e| AppError::Internal(format!("failed to begin transaction: {e}")))?;

    let match_row = update_match_result(&mut tx, &params).await?;
    let chain_job_row = upsert_chain_job(&mut tx, &params).await?;

    tx.commit()
        .await
        .map_err(|e| AppError::Internal(format!("failed to commit result transaction: {e}")))?;

    Ok(PersistResultAndEnqueueResult {
        match_status: match_row.match_status,
        chain_job_type: chain_job_row.job_type,
        chain_job_status: chain_job_row.status,
    })
}

pub async fn claim_next_due_finalizer_job(
    pool: &PgPool,
) -> Result<Option<ClaimedFinalizerJob>, AppError> {
    let mut tx = pool
        .begin()
        .await
        .map_err(|e| AppError::Internal(format!("failed to begin claim transaction: {e}")))?;

    let selected = sqlx::query(
        r#"
        select
          cj.id as chain_job_id,
          cj.match_id,
          cj.job_type,
          cj.status as chain_job_status,
          cj.winner_pubkey,
          cj.attempt_count,
          cj.last_tx_sig,
          m.game_pda,
          m.vault_pda
        from chain_jobs cj
        join matches m on m.match_id = cj.match_id
        where cj.status in ('pending', 'retrying', 'submitted')
          and cj.next_attempt_at <= now()
          and (
            cj.lock_token is null
            or cj.locked_at is null
            or cj.locked_at < now() - interval '30 seconds'
          )
        order by cj.next_attempt_at asc, cj.id asc
        for update skip locked
        limit 1
        "#,
    )
    .fetch_optional(&mut *tx)
    .await
    .map_err(|e| AppError::Internal(format!("failed to select due chain job: {e}")))?;

    let Some(row) = selected else {
        tx.commit()
            .await
            .map_err(|e| AppError::Internal(format!("failed to commit empty claim tx: {e}")))?;
        return Ok(None);
    };

    let claimed = map_claimed_job_row(row)?;
    let lock_token = Uuid::new_v4();

    sqlx::query(
        r#"
        update chain_jobs
        set lock_token = $2, locked_at = now(), updated_at = now()
        where id = $1
        "#,
    )
    .bind(claimed.chain_job_id)
    .bind(lock_token)
    .execute(&mut *tx)
    .await
    .map_err(|e| AppError::Internal(format!("failed to set chain job lock: {e}")))?;

    tx.commit()
        .await
        .map_err(|e| AppError::Internal(format!("failed to commit claim transaction: {e}")))?;

    Ok(Some(ClaimedFinalizerJob {
        chain_job_id: claimed.chain_job_id,
        match_id: claimed.match_id,
        lock_token,
        job_type: claimed.job_type,
        chain_job_status: claimed.chain_job_status,
        winner_pubkey: claimed.winner_pubkey,
        attempt_count: claimed.attempt_count,
        last_tx_sig: claimed.last_tx_sig,
        game_pda: claimed.game_pda,
        vault_pda: claimed.vault_pda,
    }))
}

pub async fn mark_job_submitted(
    pool: &PgPool,
    match_id: i64,
    lock_token: Uuid,
    tx_sig: &str,
) -> Result<(), AppError> {
    let mut tx = pool
        .begin()
        .await
        .map_err(|e| AppError::Internal(format!("failed to begin submit transaction: {e}")))?;

    let updated = sqlx::query(
        r#"
        update chain_jobs
        set
          status = 'submitted',
          last_tx_sig = $3,
          attempt_count = attempt_count + 1,
          last_error = null,
          updated_at = now()
        where match_id = $1 and lock_token = $2
        "#,
    )
    .bind(match_id)
    .bind(lock_token)
    .bind(tx_sig)
    .execute(&mut *tx)
    .await
    .map_err(|e| AppError::Internal(format!("failed to mark chain job submitted: {e}")))?;

    if updated.rows_affected() != 1 {
        return Err(AppError::Conflict(
            "chain job submit update lost lock or job no longer exists".into(),
        ));
    }

    sqlx::query(
        r#"
        update matches
        set
          match_status = case
            when match_status = 'result_pending_finalize' then 'finalizing'
            else match_status
          end,
          last_error = null,
          updated_at = now()
        where match_id = $1
        "#,
    )
    .bind(match_id)
    .execute(&mut *tx)
    .await
    .map_err(|e| AppError::Internal(format!("failed to mark match finalizing: {e}")))?;

    tx.commit()
        .await
        .map_err(|e| AppError::Internal(format!("failed to commit submit transaction: {e}")))?;

    Ok(())
}

pub async fn mark_job_retrying(
    pool: &PgPool,
    match_id: i64,
    lock_token: Uuid,
    error_message: &str,
    next_attempt_in_seconds: i64,
    increment_attempt_count: bool,
) -> Result<ChainJobStatus, AppError> {
    mark_job_retry_or_failed(
        pool,
        match_id,
        lock_token,
        "retrying",
        error_message,
        next_attempt_in_seconds,
        increment_attempt_count,
    )
    .await
}

pub async fn mark_job_failed(
    pool: &PgPool,
    match_id: i64,
    lock_token: Uuid,
    error_message: &str,
    increment_attempt_count: bool,
) -> Result<ChainJobStatus, AppError> {
    mark_job_retry_or_failed(
        pool,
        match_id,
        lock_token,
        "failed",
        error_message,
        0,
        increment_attempt_count,
    )
    .await
}

pub async fn mark_job_confirmed_and_finalize_match(
    pool: &PgPool,
    match_id: i64,
    lock_token: Uuid,
    final_tx_sig: Option<&str>,
    final_match_status: MatchStatus,
) -> Result<(), AppError> {
    let final_match_status_db = match_status_to_final_db(final_match_status)?;

    let mut tx = pool
        .begin()
        .await
        .map_err(|e| AppError::Internal(format!("failed to begin confirm transaction: {e}")))?;

    let updated_job = sqlx::query(
        r#"
        update chain_jobs
        set
          status = 'confirmed',
          last_tx_sig = coalesce(last_tx_sig, $3),
          last_error = null,
          lock_token = null,
          locked_at = null,
          updated_at = now()
        where match_id = $1 and lock_token = $2
        "#,
    )
    .bind(match_id)
    .bind(lock_token)
    .bind(final_tx_sig)
    .execute(&mut *tx)
    .await
    .map_err(|e| AppError::Internal(format!("failed to mark chain job confirmed: {e}")))?;

    if updated_job.rows_affected() != 1 {
        return Err(AppError::Conflict(
            "chain job confirm update lost lock or job no longer exists".into(),
        ));
    }

    sqlx::query(
        r#"
        update matches
        set
          match_status = $2,
          final_tx_sig = coalesce(final_tx_sig, $3),
          finalized_at = coalesce(finalized_at, now()),
          last_error = null,
          updated_at = now()
        where match_id = $1
        "#,
    )
    .bind(match_id)
    .bind(final_match_status_db)
    .bind(final_tx_sig)
    .execute(&mut *tx)
    .await
    .map_err(|e| AppError::Internal(format!("failed to finalize match status: {e}")))?;

    tx.commit()
        .await
        .map_err(|e| AppError::Internal(format!("failed to commit confirm transaction: {e}")))?;

    Ok(())
}

pub async fn clear_job_lock(
    pool: &PgPool,
    match_id: i64,
    lock_token: Uuid,
) -> Result<(), AppError> {
    sqlx::query(
        r#"
        update chain_jobs
        set lock_token = null, locked_at = null, updated_at = now()
        where match_id = $1 and lock_token = $2
        "#,
    )
    .bind(match_id)
    .bind(lock_token)
    .execute(pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to clear chain job lock: {e}")))?;
    Ok(())
}

async fn update_match_result(
    tx: &mut sqlx::Transaction<'_, sqlx::Postgres>,
    params: &PersistResultAndEnqueueParams,
) -> Result<MatchResultUpdateRow, AppError> {
    let now = Utc::now();

    let row = sqlx::query(
        r#"
        update matches
        set
          match_status = case
            when match_status in ('joined_on_chain', 'in_progress') then 'result_pending_finalize'
            else match_status
          end,
          finalization_reason_code = coalesce(finalization_reason_code, $2),
          finalization_reason_detail = coalesce(finalization_reason_detail, $3),
          winner_pubkey = coalesce(winner_pubkey, $4),
          result_idempotency_key = coalesce(result_idempotency_key, $5),
          result_reported_at = coalesce(result_reported_at, $6),
          updated_at = $6
        where match_id = $1
          and (
            match_status in ('joined_on_chain', 'in_progress')
            or result_idempotency_key = $5
          )
          and (result_idempotency_key is null or result_idempotency_key = $5)
          and (winner_pubkey is null or winner_pubkey is not distinct from $4)
          and (finalization_reason_code is null or finalization_reason_code = $2)
        returning match_status
        "#,
    )
    .bind(params.match_id)
    .bind(&params.reason_code)
    .bind(&params.reason_detail)
    .bind(&params.winner_pubkey)
    .bind(&params.idempotency_key)
    .bind(now)
    .fetch_optional(&mut **tx)
    .await
    .map_err(|e| AppError::Internal(format!("failed to persist match result: {e}")))?;

    let row = row.ok_or_else(|| {
        AppError::Conflict(
            "result conflicts with existing match finalization state or idempotency key".into(),
        )
    })?;

    Ok(MatchResultUpdateRow {
        match_status: parse_match_status(row.get::<String, _>("match_status").as_str())?,
    })
}

async fn upsert_chain_job(
    tx: &mut sqlx::Transaction<'_, sqlx::Postgres>,
    params: &PersistResultAndEnqueueParams,
) -> Result<ChainJobUpsertRow, AppError> {
    let row = sqlx::query(
        r#"
        insert into chain_jobs (
          match_id,
          job_type,
          status,
          winner_pubkey,
          next_attempt_at
        )
        values ($1, $2, 'pending', $3, now())
        on conflict (match_id) do update
          set updated_at = now()
        where chain_jobs.job_type = excluded.job_type
          and chain_jobs.winner_pubkey is not distinct from excluded.winner_pubkey
        returning job_type, status
        "#,
    )
    .bind(params.match_id)
    .bind(chain_job_type_to_db(params.job_type))
    .bind(&params.winner_pubkey)
    .fetch_optional(&mut **tx)
    .await
    .map_err(|e| AppError::Internal(format!("failed to upsert chain job: {e}")))?;

    let row = row.ok_or_else(|| {
        AppError::Conflict("existing chain job conflicts with submitted result".into())
    })?;

    Ok(ChainJobUpsertRow {
        job_type: parse_chain_job_type(row.get::<String, _>("job_type").as_str())?,
        status: parse_chain_job_status(row.get::<String, _>("status").as_str())?,
    })
}

fn chain_job_type_to_db(value: ChainJobType) -> &'static str {
    match value {
        ChainJobType::Settle => "settle",
        ChainJobType::ForceRefund => "force_refund",
    }
}

fn match_status_to_final_db(value: MatchStatus) -> Result<&'static str, AppError> {
    match value {
        MatchStatus::Settled => Ok("settled"),
        MatchStatus::Refunded => Ok("refunded"),
        _ => Err(AppError::Internal(format!(
            "final match status must be settled/refunded, got {:?}",
            value
        ))),
    }
}

fn map_claimed_job_row(row: sqlx::postgres::PgRow) -> Result<ClaimedJobSelectRow, AppError> {
    Ok(ClaimedJobSelectRow {
        chain_job_id: row.get::<i64, _>("chain_job_id"),
        match_id: row.get::<i64, _>("match_id"),
        job_type: parse_chain_job_type(row.get::<String, _>("job_type").as_str())?,
        chain_job_status: parse_chain_job_status(
            row.get::<String, _>("chain_job_status").as_str(),
        )?,
        winner_pubkey: row.get::<Option<String>, _>("winner_pubkey"),
        attempt_count: row.get::<i32, _>("attempt_count"),
        last_tx_sig: row.get::<Option<String>, _>("last_tx_sig"),
        game_pda: row.get::<String, _>("game_pda"),
        vault_pda: row.get::<String, _>("vault_pda"),
    })
}

async fn mark_job_retry_or_failed(
    pool: &PgPool,
    match_id: i64,
    lock_token: Uuid,
    next_status_db: &str,
    error_message: &str,
    next_attempt_in_seconds: i64,
    increment_attempt_count: bool,
) -> Result<ChainJobStatus, AppError> {
    let next_status = parse_chain_job_status(next_status_db)?;
    let mut tx = pool
        .begin()
        .await
        .map_err(|e| AppError::Internal(format!("failed to begin retry/fail transaction: {e}")))?;

    let row = sqlx::query(
        r#"
        update chain_jobs
        set
          status = $3,
          last_error = $4,
          next_attempt_at = case
            when $3 = 'retrying' then now() + ($5::int * interval '1 second')
            else next_attempt_at
          end,
          attempt_count = case when $6 then attempt_count + 1 else attempt_count end,
          lock_token = null,
          locked_at = null,
          updated_at = now()
        where match_id = $1 and lock_token = $2
        returning status
        "#,
    )
    .bind(match_id)
    .bind(lock_token)
    .bind(next_status_db)
    .bind(error_message)
    .bind(next_attempt_in_seconds)
    .bind(increment_attempt_count)
    .fetch_optional(&mut *tx)
    .await
    .map_err(|e| AppError::Internal(format!("failed to update chain job retry/fail state: {e}")))?;

    let row = row.ok_or_else(|| {
        AppError::Conflict("chain job retry/fail update lost lock or job no longer exists".into())
    })?;

    sqlx::query(
        r#"
        update matches
        set
          match_status = case
            when match_status in ('result_pending_finalize', 'finalizing') and $2 = 'retrying'
              then 'finalizing'
            else match_status
          end,
          last_error = $3,
          updated_at = now()
        where match_id = $1
        "#,
    )
    .bind(match_id)
    .bind(next_status_db)
    .bind(error_message)
    .execute(&mut *tx)
    .await
    .map_err(|e| AppError::Internal(format!("failed to update match error state: {e}")))?;

    tx.commit()
        .await
        .map_err(|e| AppError::Internal(format!("failed to commit retry/fail transaction: {e}")))?;

    parse_chain_job_status(row.get::<String, _>("status").as_str()).or(Ok(next_status))
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
                "unknown matches.match_status in DB: {raw}"
            )))
        }
    };
    Ok(status)
}

fn parse_chain_job_type(raw: &str) -> Result<ChainJobType, AppError> {
    let value = match raw {
        "settle" => ChainJobType::Settle,
        "force_refund" => ChainJobType::ForceRefund,
        _ => {
            return Err(AppError::Internal(format!(
                "unknown chain_jobs.job_type in DB: {raw}"
            )))
        }
    };
    Ok(value)
}

fn parse_chain_job_status(raw: &str) -> Result<ChainJobStatus, AppError> {
    let value = match raw {
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
    Ok(value)
}
