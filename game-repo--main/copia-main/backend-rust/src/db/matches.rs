//! DB helpers for `matches`.

use sqlx::{PgPool, Row};

use crate::error::AppError;

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
