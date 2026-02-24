use axum::{
    extract::{Path, State},
    routing::{get, post},
    Json, Router,
};
use chrono::Utc;

use crate::{
    app_state::AppState,
    db::matches as matches_db,
    error::AppError,
    models::{
        dto::{
            CreateConfirmRequest, CreateConfirmResponse, CreateMatchRequest, CreateMatchResponse,
            JoinConfirmRequest, JoinConfirmResponse, MatchLookupByCodeResponse,
            MatchStatusResponse, ResultRequest, ResultResponse,
        },
        enums::{ChainJobStatus, ChainJobType, MatchStatus, ResultOutcome},
    },
    solana::pda::derive_match_pdas,
};

pub fn router() -> Router<AppState> {
    Router::new()
        .route("/", post(create_match))
        .route("/code/:join_code", get(get_match_by_code))
        .route("/:match_id/create-confirm", post(confirm_create_tx))
        .route("/:match_id/join-confirm", post(confirm_join_tx))
        .route("/:match_id/result", post(submit_result))
        .route("/:match_id/status", get(get_match_status))
}

async fn create_match(
    State(state): State<AppState>,
    Json(payload): Json<CreateMatchRequest>,
) -> Result<Json<CreateMatchResponse>, AppError> {
    let player1_pubkey = payload.player1_pubkey.trim();
    if player1_pubkey.is_empty() {
        return Err(AppError::BadRequest("player1_pubkey is required".into()));
    }

    let entry_lamports_u64 = payload.entry_lamports.parse::<u64>().map_err(|_| {
        AppError::BadRequest("entry_lamports must be a positive integer string".into())
    })?;
    if entry_lamports_u64 == 0 {
        return Err(AppError::BadRequest("entry_lamports must be > 0".into()));
    }
    if entry_lamports_u64 > i64::MAX as u64 {
        return Err(AppError::BadRequest(
            "entry_lamports is too large for backend storage".into(),
        ));
    }
    let entry_lamports_i64 = entry_lamports_u64 as i64;

    let match_id = matches_db::reserve_next_match_id(&state.pool).await?;
    let join_code = matches_db::join_code_from_match_id(match_id)?;
    let pdas = derive_match_pdas(
        &state.config.program_id,
        &state.config.authority_pubkey,
        player1_pubkey,
        match_id,
    )
    .map_err(|e| AppError::BadRequest(format!("invalid create-match inputs: {e}")))?;

    let created = matches_db::insert_create_match_record(
        &state.pool,
        match_id,
        &join_code,
        &matches_db::CreateMatchRecordParams {
            program_id: &state.config.program_id,
            authority_pubkey: &state.config.authority_pubkey,
            player1_pubkey,
            entry_lamports: entry_lamports_i64,
            game_pda: &pdas.game_pda,
            vault_pda: &pdas.vault_pda,
        },
    )
    .await?;

    Ok(Json(CreateMatchResponse {
        match_id: created.match_id.to_string(),
        join_code: created.join_code,
        program_id: state.config.program_id.clone(),
        authority_pubkey: state.config.authority_pubkey.clone(),
        game_pda: pdas.game_pda,
        vault_pda: pdas.vault_pda,
        entry_lamports: entry_lamports_u64.to_string(),
        join_timeout_seconds: state.config.join_timeout_seconds,
        settle_timeout_seconds: state.config.settle_timeout_seconds,
        match_status: MatchStatus::WaitingCreateTx,
    }))
}

async fn get_match_by_code(
    State(_state): State<AppState>,
    Path(join_code): Path<String>,
) -> Result<Json<MatchLookupByCodeResponse>, AppError> {
    if join_code.is_empty() {
        return Err(AppError::BadRequest("join_code is required".into()));
    }

    // TODO: load match from DB by join_code.
    Ok(Json(MatchLookupByCodeResponse {
        match_id: "0".into(),
        join_code,
        game_pda: "TODO_GAME_PDA".into(),
        vault_pda: "TODO_VAULT_PDA".into(),
        player1_pubkey: "TODO_PLAYER1".into(),
        entry_lamports: "0".into(),
        match_status: MatchStatus::CreatedOnChain,
        join_expires_at: None,
    }))
}

async fn confirm_create_tx(
    State(_state): State<AppState>,
    Path(match_id): Path<String>,
    Json(payload): Json<CreateConfirmRequest>,
) -> Result<Json<CreateConfirmResponse>, AppError> {
    if payload.create_tx_sig.is_empty() {
        return Err(AppError::BadRequest("create_tx_sig is required".into()));
    }

    // TODO: verify on-chain Game account at expected PDA and update DB state.
    Ok(Json(CreateConfirmResponse {
        match_id,
        verified: false,
        match_status: MatchStatus::CreatedOnChain,
        create_tx_sig: payload.create_tx_sig,
        join_expires_at: None,
    }))
}

async fn confirm_join_tx(
    State(_state): State<AppState>,
    Path(match_id): Path<String>,
    Json(payload): Json<JoinConfirmRequest>,
) -> Result<Json<JoinConfirmResponse>, AppError> {
    if payload.join_tx_sig.is_empty() {
        return Err(AppError::BadRequest("join_tx_sig is required".into()));
    }

    // TODO: verify on-chain joined state, capture player2 pubkey, update DB.
    Ok(Json(JoinConfirmResponse {
        match_id,
        verified: false,
        match_status: MatchStatus::JoinedOnChain,
        player2_pubkey: "TODO_PLAYER2".into(),
        join_tx_sig: payload.join_tx_sig,
        settle_expires_at: None,
    }))
}

async fn submit_result(
    State(state): State<AppState>,
    Path(match_id): Path<String>,
    Json(payload): Json<ResultRequest>,
) -> Result<Json<ResultResponse>, AppError> {
    validate_internal_headers_stub(&state)?;

    match payload.outcome {
        ResultOutcome::Winner => {
            if payload.winner_pubkey.as_deref().unwrap_or("").is_empty() {
                return Err(AppError::BadRequest(
                    "winner_pubkey is required when outcome=winner".into(),
                ));
            }
        }
        ResultOutcome::Broken => {}
    }
    if payload.idempotency_key.is_empty() {
        return Err(AppError::BadRequest("idempotency_key is required".into()));
    }

    // TODO: enqueue chain_jobs row (settle or force_refund) and update matches state.
    let finalization_action = match payload.outcome {
        ResultOutcome::Winner => ChainJobType::Settle,
        ResultOutcome::Broken => ChainJobType::ForceRefund,
    };

    Ok(Json(ResultResponse {
        match_id,
        match_status: MatchStatus::ResultPendingFinalize,
        finalization_action,
        chain_job_status: ChainJobStatus::Pending,
    }))
}

async fn get_match_status(
    State(_state): State<AppState>,
    Path(match_id): Path<String>,
) -> Result<Json<MatchStatusResponse>, AppError> {
    // TODO: join matches + chain_jobs and return current record for Unity polling.
    Ok(Json(MatchStatusResponse {
        match_id,
        join_code: "TODO".into(),
        program_id: "3abFWCLDDyA2jHfnGLQUTX6W9jddXSMHt9jtyc6Xjfjc".into(),
        authority_pubkey: "8m2D5QJjQbGEMfFKcjGmdf4xmwWrjZGuoiASpXWM6yJG".into(),
        game_pda: "TODO_GAME_PDA".into(),
        vault_pda: "TODO_VAULT_PDA".into(),
        player1_pubkey: "TODO_PLAYER1".into(),
        player2_pubkey: None,
        entry_lamports: "0".into(),
        pot_lamports: "0".into(),
        match_status: MatchStatus::WaitingCreateTx,
        chain_job_type: None,
        chain_job_status: None,
        winner_pubkey: None,
        finalization_reason_code: None,
        create_tx_sig: None,
        join_tx_sig: None,
        final_tx_sig: None,
        join_expires_at: None,
        settle_expires_at: None,
        last_error: None,
        updated_at: Utc::now(),
    }))
}

fn validate_internal_headers_stub(state: &AppState) -> Result<(), AppError> {
    if state.config.internal_hmac_secret.is_empty() {
        return Err(AppError::Unauthorized);
    }
    // TODO: verify HMAC headers (timestamp + nonce + signature) on internal/admin routes.
    Ok(())
}
