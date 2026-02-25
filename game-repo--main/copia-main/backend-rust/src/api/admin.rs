use axum::{
    body::Bytes,
    extract::{Path, State},
    http::HeaderMap,
    routing::post,
    Json, Router,
};

use crate::{
    app_state::AppState,
    db::chain_jobs as chain_jobs_db,
    error::AppError,
    models::dto::{RetryFinalizationRequest, RetryFinalizationResponse},
};

pub fn router() -> Router<AppState> {
    Router::new().route(
        "/matches/:match_id/retry-finalization",
        post(retry_finalization),
    )
}

async fn retry_finalization(
    State(state): State<AppState>,
    Path(match_id): Path<String>,
    headers: HeaderMap,
    body: Bytes,
) -> Result<Json<RetryFinalizationResponse>, AppError> {
    crate::api::internal_auth::verify_internal_hmac(&state, &headers, body.as_ref()).await?;
    let payload: RetryFinalizationRequest = serde_json::from_slice(body.as_ref())
        .map_err(|e| AppError::BadRequest(format!("invalid JSON body: {e}")))?;

    if payload.reason.trim().is_empty() {
        return Err(AppError::BadRequest("reason is required".into()));
    }

    let match_id_i64 = parse_match_id(&match_id)?;
    let retried = chain_jobs_db::retry_finalization_job(&state.pool, match_id_i64).await?;

    Ok(Json(RetryFinalizationResponse {
        match_id: match_id_i64.to_string(),
        match_status: retried.match_status,
        chain_job_status: retried.chain_job_status,
    }))
}

fn parse_match_id(raw: &str) -> Result<i64, AppError> {
    let value = raw
        .trim()
        .parse::<i64>()
        .map_err(|_| AppError::BadRequest("match_id must be an integer".into()))?;
    if value <= 0 {
        return Err(AppError::BadRequest("match_id must be positive".into()));
    }
    Ok(value)
}
