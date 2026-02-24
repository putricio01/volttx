use axum::{
    extract::{Path, State},
    routing::{get, post},
    Json, Router,
};
use chrono::{Duration, TimeZone, Utc};
use solana_sdk::pubkey::Pubkey;

use crate::{
    app_state::AppState,
    db::chain_jobs as chain_jobs_db,
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
    solana::{
        client::fetch_and_decode_game_account, game_account::DecodedGameState,
        pda::derive_match_pdas,
    },
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
    State(state): State<AppState>,
    Path(join_code): Path<String>,
) -> Result<Json<MatchLookupByCodeResponse>, AppError> {
    let join_code = join_code.trim().to_ascii_uppercase();
    if join_code.is_empty() {
        return Err(AppError::BadRequest("join_code is required".into()));
    }

    let row = matches_db::get_match_lookup_by_join_code(&state.pool, &join_code)
        .await?
        .ok_or_else(|| AppError::NotFound("match".into()))?;

    if matches!(
        row.match_status,
        MatchStatus::Settled | MatchStatus::Refunded
    ) {
        return Err(AppError::Conflict("match is no longer active".into()));
    }

    Ok(Json(MatchLookupByCodeResponse {
        match_id: row.match_id.to_string(),
        join_code: row.join_code,
        game_pda: row.game_pda,
        vault_pda: row.vault_pda,
        player1_pubkey: row.player1_pubkey,
        entry_lamports: row.entry_lamports.to_string(),
        match_status: row.match_status,
        join_expires_at: row.join_expires_at,
    }))
}

async fn confirm_create_tx(
    State(state): State<AppState>,
    Path(match_id): Path<String>,
    Json(payload): Json<CreateConfirmRequest>,
) -> Result<Json<CreateConfirmResponse>, AppError> {
    if payload.create_tx_sig.is_empty() {
        return Err(AppError::BadRequest("create_tx_sig is required".into()));
    }

    let match_id_i64 = parse_match_id(&match_id)?;
    let row = matches_db::get_match_for_create_confirm(&state.pool, match_id_i64)
        .await?
        .ok_or_else(|| AppError::NotFound("match".into()))?;

    // Idempotent path: once create was already verified (or match progressed), return current state.
    if !matches!(row.match_status, MatchStatus::WaitingCreateTx) {
        return Ok(Json(CreateConfirmResponse {
            match_id: row.match_id.to_string(),
            verified: true,
            match_status: row.match_status,
            create_tx_sig: row
                .create_tx_sig
                .unwrap_or_else(|| payload.create_tx_sig.clone()),
            join_expires_at: row.join_expires_at,
        }));
    }

    let decoded = fetch_and_decode_game_account(
        &state.config.solana_rpc_url,
        &state.config.program_id,
        &row.game_pda,
    )
    .await
    .map_err(|e| AppError::BadRequest(format!("failed to verify on-chain game account: {e}")))?;

    if decoded.state != DecodedGameState::Created {
        return Err(AppError::Conflict(
            "game account is not in Created state".into(),
        ));
    }
    if decoded.player1.to_string() != row.player1_pubkey {
        return Err(AppError::Conflict(
            "game.player1 does not match backend record".into(),
        ));
    }
    if decoded.authority.to_string() != row.authority_pubkey {
        return Err(AppError::Conflict(
            "game.authority does not match backend record".into(),
        ));
    }
    if decoded.match_id != row.match_id as u64 {
        return Err(AppError::Conflict(
            "game.match_id does not match backend record".into(),
        ));
    }
    if decoded.entry_amount != row.entry_lamports as u64 {
        return Err(AppError::Conflict(
            "game.entry_amount does not match backend record".into(),
        ));
    }

    let created_onchain_at = Utc
        .timestamp_opt(decoded.created_at, 0)
        .single()
        .ok_or_else(|| AppError::Internal("invalid on-chain created_at timestamp".into()))?;
    let join_expires_at = created_onchain_at + Duration::seconds(state.config.join_timeout_seconds);

    let updated = matches_db::mark_match_created_on_chain(
        &state.pool,
        row.match_id,
        &payload.create_tx_sig,
        created_onchain_at,
        join_expires_at,
    )
    .await?;

    Ok(Json(CreateConfirmResponse {
        match_id: updated.match_id.to_string(),
        verified: true,
        match_status: updated.match_status,
        create_tx_sig: updated
            .create_tx_sig
            .unwrap_or_else(|| payload.create_tx_sig.clone()),
        join_expires_at: updated.join_expires_at,
    }))
}

async fn confirm_join_tx(
    State(state): State<AppState>,
    Path(match_id): Path<String>,
    Json(payload): Json<JoinConfirmRequest>,
) -> Result<Json<JoinConfirmResponse>, AppError> {
    if payload.join_tx_sig.is_empty() {
        return Err(AppError::BadRequest("join_tx_sig is required".into()));
    }

    let match_id_i64 = parse_match_id(&match_id)?;
    let row = matches_db::get_match_for_join_confirm(&state.pool, match_id_i64)
        .await?
        .ok_or_else(|| AppError::NotFound("match".into()))?;

    if !matches!(row.match_status, MatchStatus::CreatedOnChain) {
        if matches!(
            row.match_status,
            MatchStatus::JoinedOnChain
                | MatchStatus::InProgress
                | MatchStatus::ResultPendingFinalize
                | MatchStatus::Finalizing
                | MatchStatus::Settled
                | MatchStatus::Refunded
        ) {
            let player2_pubkey = row.player2_pubkey.ok_or_else(|| {
                AppError::Conflict("match is past join stage but player2 is missing".into())
            })?;
            return Ok(Json(JoinConfirmResponse {
                match_id: row.match_id.to_string(),
                verified: true,
                match_status: row.match_status,
                player2_pubkey,
                join_tx_sig: row.join_tx_sig.unwrap_or(payload.join_tx_sig),
                settle_expires_at: row.settle_expires_at,
            }));
        }

        return Err(AppError::Conflict(
            "match is not ready for join-confirm (create not verified yet)".into(),
        ));
    }

    let decoded = fetch_and_decode_game_account(
        &state.config.solana_rpc_url,
        &state.config.program_id,
        &row.game_pda,
    )
    .await
    .map_err(|e| AppError::BadRequest(format!("failed to verify on-chain game account: {e}")))?;

    if decoded.state != DecodedGameState::Joined {
        return Err(AppError::Conflict(
            "game account is not in Joined state".into(),
        ));
    }
    if decoded.player1.to_string() != row.player1_pubkey {
        return Err(AppError::Conflict(
            "game.player1 does not match backend record".into(),
        ));
    }
    if decoded.authority.to_string() != row.authority_pubkey {
        return Err(AppError::Conflict(
            "game.authority does not match backend record".into(),
        ));
    }
    if decoded.match_id != row.match_id as u64 {
        return Err(AppError::Conflict(
            "game.match_id does not match backend record".into(),
        ));
    }
    if decoded.entry_amount != row.entry_lamports as u64 {
        return Err(AppError::Conflict(
            "game.entry_amount does not match backend record".into(),
        ));
    }
    if decoded.player2 == Pubkey::default() {
        return Err(AppError::Conflict(
            "game.player2 is still default pubkey".into(),
        ));
    }
    if decoded.player2 == decoded.player1 {
        return Err(AppError::Conflict(
            "game.player2 cannot equal game.player1".into(),
        ));
    }

    let joined_onchain_at = Utc
        .timestamp_opt(decoded.joined_at, 0)
        .single()
        .ok_or_else(|| AppError::Internal("invalid on-chain joined_at timestamp".into()))?;
    let settle_expires_at =
        joined_onchain_at + Duration::seconds(state.config.settle_timeout_seconds);
    let player2_pubkey = decoded.player2.to_string();

    let updated = matches_db::mark_match_joined_on_chain(
        &state.pool,
        row.match_id,
        &player2_pubkey,
        &payload.join_tx_sig,
        joined_onchain_at,
        settle_expires_at,
    )
    .await?;

    Ok(Json(JoinConfirmResponse {
        match_id: updated.match_id.to_string(),
        verified: true,
        match_status: updated.match_status,
        player2_pubkey: updated
            .player2_pubkey
            .unwrap_or_else(|| player2_pubkey.clone()),
        join_tx_sig: updated.join_tx_sig.unwrap_or(payload.join_tx_sig),
        settle_expires_at: updated.settle_expires_at,
    }))
}

async fn submit_result(
    State(state): State<AppState>,
    Path(match_id): Path<String>,
    Json(payload): Json<ResultRequest>,
) -> Result<Json<ResultResponse>, AppError> {
    validate_internal_headers_stub(&state)?;

    let match_id_i64 = parse_match_id(&match_id)?;
    let idempotency_key = payload.idempotency_key.trim();
    if idempotency_key.is_empty() {
        return Err(AppError::BadRequest("idempotency_key is required".into()));
    }
    let reason_code = payload.reason_code.trim();
    if reason_code.is_empty() {
        return Err(AppError::BadRequest("reason_code is required".into()));
    }
    let reason_detail = payload
        .reason_detail
        .as_deref()
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .map(ToOwned::to_owned);

    let match_row = matches_db::get_match_status_record(&state.pool, match_id_i64)
        .await?
        .ok_or_else(|| AppError::NotFound("match".into()))?;

    if matches!(
        match_row.match_status,
        MatchStatus::WaitingCreateTx | MatchStatus::CreatedOnChain
    ) {
        return Err(AppError::Conflict(
            "match is not ready for result submission".into(),
        ));
    }

    let winner_pubkey = match payload.outcome {
        ResultOutcome::Winner => {
            let winner = payload.winner_pubkey.as_deref().unwrap_or("").trim();
            if winner.is_empty() {
                return Err(AppError::BadRequest(
                    "winner_pubkey is required when outcome=winner".into(),
                ));
            }
            let player2 = match_row
                .player2_pubkey
                .as_deref()
                .ok_or_else(|| AppError::Conflict("match has no player2 recorded yet".into()))?;
            if winner != match_row.player1_pubkey && winner != player2 {
                return Err(AppError::Conflict(
                    "winner_pubkey must match player1 or player2".into(),
                ));
            }
            Some(winner.to_string())
        }
        ResultOutcome::Broken => {
            if payload
                .winner_pubkey
                .as_deref()
                .map(str::trim)
                .is_some_and(|s| !s.is_empty())
            {
                return Err(AppError::BadRequest(
                    "winner_pubkey must be omitted when outcome=broken".into(),
                ));
            }
            None
        }
    };

    let finalization_action = match payload.outcome {
        ResultOutcome::Winner => ChainJobType::Settle,
        ResultOutcome::Broken => ChainJobType::ForceRefund,
    };
    let persisted = chain_jobs_db::persist_result_and_enqueue(
        &state.pool,
        chain_jobs_db::PersistResultAndEnqueueParams {
            match_id: match_id_i64,
            job_type: finalization_action,
            winner_pubkey,
            reason_code: reason_code.to_string(),
            reason_detail,
            idempotency_key: idempotency_key.to_string(),
        },
    )
    .await?;

    Ok(Json(ResultResponse {
        match_id: match_id_i64.to_string(),
        match_status: persisted.match_status,
        finalization_action: persisted.chain_job_type,
        chain_job_status: persisted.chain_job_status,
    }))
}

async fn get_match_status(
    State(state): State<AppState>,
    Path(match_id): Path<String>,
) -> Result<Json<MatchStatusResponse>, AppError> {
    let match_id_i64 = parse_match_id(&match_id)?;
    let row = matches_db::get_match_status_record(&state.pool, match_id_i64)
        .await?
        .ok_or_else(|| AppError::NotFound("match".into()))?;

    let entry_u64 = u64::try_from(row.entry_lamports)
        .map_err(|_| AppError::Internal("entry_lamports in DB is negative".into()))?;
    let pot_u64 = entry_u64
        .checked_mul(2)
        .ok_or_else(|| AppError::Internal("pot calculation overflow".into()))?;

    Ok(Json(MatchStatusResponse {
        match_id: row.match_id.to_string(),
        join_code: row.join_code,
        program_id: row.program_id,
        authority_pubkey: row.authority_pubkey,
        game_pda: row.game_pda,
        vault_pda: row.vault_pda,
        player1_pubkey: row.player1_pubkey,
        player2_pubkey: row.player2_pubkey,
        entry_lamports: entry_u64.to_string(),
        pot_lamports: pot_u64.to_string(),
        match_status: row.match_status,
        chain_job_type: row.chain_job_type,
        chain_job_status: row.chain_job_status,
        winner_pubkey: row.winner_pubkey,
        finalization_reason_code: row.finalization_reason_code,
        create_tx_sig: row.create_tx_sig,
        join_tx_sig: row.join_tx_sig,
        final_tx_sig: row.final_tx_sig,
        join_expires_at: row.join_expires_at,
        settle_expires_at: row.settle_expires_at,
        last_error: row.last_error,
        updated_at: row.updated_at,
    }))
}

fn validate_internal_headers_stub(state: &AppState) -> Result<(), AppError> {
    if state.config.internal_hmac_secret.is_empty() {
        return Err(AppError::Unauthorized);
    }
    // TODO: verify HMAC headers (timestamp + nonce + signature) on internal/admin routes.
    Ok(())
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
