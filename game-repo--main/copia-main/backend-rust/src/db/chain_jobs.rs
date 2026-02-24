//! DB helpers for `chain_jobs`.
//!
//! This module will own:
//! - enqueue finalization job
//! - lock next due job (`FOR UPDATE SKIP LOCKED`)
//! - mark submitted/retrying/confirmed/failed

use chrono::{DateTime, Utc};
use sqlx::{PgPool, Row};

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

#[derive(Debug)]
struct MatchResultUpdateRow {
    match_status: MatchStatus,
}

#[derive(Debug)]
struct ChainJobUpsertRow {
    job_type: ChainJobType,
    status: ChainJobStatus,
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
