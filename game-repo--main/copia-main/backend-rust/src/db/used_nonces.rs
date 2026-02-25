use sqlx::PgPool;

use crate::error::AppError;

pub async fn insert_nonce_if_unused(pool: &PgPool, nonce: &str) -> Result<bool, AppError> {
    let result = sqlx::query(
        r#"
        insert into used_nonces (nonce)
        values ($1)
        on conflict (nonce) do nothing
        "#,
    )
    .bind(nonce)
    .execute(pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to persist nonce: {e}")))?;

    Ok(result.rows_affected() == 1)
}
