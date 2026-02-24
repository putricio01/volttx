use axum::{
    extract::{Path, State},
    routing::post,
    Json, Router,
};

use crate::{
    app_state::AppState,
    error::AppError,
    models::{
        dto::{RetryFinalizationRequest, RetryFinalizationResponse},
        enums::{ChainJobStatus, MatchStatus},
    },
};

pub fn router() -> Router<AppState> {
    Router::new().route("/matches/:match_id/retry-finalization", post(retry_finalization))
}

async fn retry_finalization(
    State(state): State<AppState>,
    Path(match_id): Path<String>,
    Json(payload): Json<RetryFinalizationRequest>,
) -> Result<Json<RetryFinalizationResponse>, AppError> {
    if payload.reason.trim().is_empty() {
        return Err(AppError::BadRequest("reason is required".into()));
    }
    if state.config.internal_hmac_secret.is_empty() {
        return Err(AppError::Unauthorized);
    }

    // TODO: authenticate admin request and reset chain_jobs.status='pending'.
    Ok(Json(RetryFinalizationResponse {
        match_id,
        match_status: MatchStatus::Finalizing,
        chain_job_status: ChainJobStatus::Pending,
    }))
}

