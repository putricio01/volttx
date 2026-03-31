use axum::{body::Bytes, extract::State, http::HeaderMap, routing::post, Json, Router};
use chrono::{TimeZone, Utc};
use solana_sdk::pubkey::Pubkey;

use crate::{
    app_state::AppState,
    db::chain_jobs as chain_jobs_db,
    db::matches as matches_db,
    error::AppError,
    models::{
        dto::{FinalizeRequest, FinalizeResponse},
        enums::{ChainJobType, MatchStatus, ResultOutcome},
    },
    solana::{
        client::fetch_and_decode_game_account, game_account::DecodedGameState,
        pda::derive_match_pdas,
    },
};

pub fn router() -> Router<AppState> {
    Router::new().route("/finalize", post(finalize))
}

async fn finalize(
    State(state): State<AppState>,
    headers: HeaderMap,
    body: Bytes,
) -> Result<Json<FinalizeResponse>, AppError> {
    crate::api::internal_auth::verify_internal_hmac(&state, &headers, body.as_ref()).await?;
    let payload: FinalizeRequest = serde_json::from_slice(body.as_ref())
        .map_err(|e| AppError::BadRequest(format!("invalid JSON body: {e}")))?;

    let game_pda = payload.game_pda.trim();
    if game_pda.is_empty() {
        return Err(AppError::BadRequest("game_pda is required".into()));
    }

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

    let decoded = fetch_and_decode_game_account(
        &state.config.solana_rpc_url,
        &state.config.program_id,
        game_pda,
    )
    .await
    .map_err(|e| AppError::BadRequest(format!("failed to verify on-chain game account: {e}")))?;

    let authority_pubkey = decoded.authority.to_string();
    if authority_pubkey != state.config.authority_pubkey {
        return Err(AppError::Conflict(
            "game.authority does not match backend AUTHORITY_PUBKEY".into(),
        ));
    }

    let match_id_i64 = i64::try_from(decoded.match_id)
        .map_err(|_| AppError::Conflict("game.match_id is too large for backend storage".into()))?;
    if match_id_i64 <= 0 {
        return Err(AppError::Conflict("game.match_id must be positive".into()));
    }
    let entry_lamports_i64 = i64::try_from(decoded.entry_amount).map_err(|_| {
        AppError::Conflict("game.entry_amount is too large for backend storage".into())
    })?;
    if entry_lamports_i64 <= 0 {
        return Err(AppError::Conflict("game.entry_amount must be > 0".into()));
    }

    let player1_pubkey = decoded.player1.to_string();
    let expected_pdas = derive_match_pdas(
        &state.config.program_id,
        &authority_pubkey,
        &player1_pubkey,
        match_id_i64,
    )
    .map_err(|e| AppError::Internal(format!("failed to derive expected match PDAs: {e}")))?;
    if expected_pdas.game_pda != game_pda {
        return Err(AppError::Conflict(
            "provided game_pda does not match canonical PDA for this game account".into(),
        ));
    }

    let created_onchain_at = Utc
        .timestamp_opt(decoded.created_at, 0)
        .single()
        .ok_or_else(|| AppError::Internal("invalid on-chain created_at timestamp".into()))?;

    let mut player2_pubkey: Option<String> = None;
    let mut joined_onchain_at = None;
    let inferred_match_status = match decoded.state {
        DecodedGameState::Created => MatchStatus::CreatedOnChain,
        DecodedGameState::Joined => {
            if decoded.player2 == Pubkey::default() {
                return Err(AppError::Conflict(
                    "game.player2 is default while state=Joined".into(),
                ));
            }
            if decoded.player2 == decoded.player1 {
                return Err(AppError::Conflict(
                    "game.player2 cannot equal game.player1".into(),
                ));
            }

            let joined_at = Utc
                .timestamp_opt(decoded.joined_at, 0)
                .single()
                .ok_or_else(|| AppError::Internal("invalid on-chain joined_at timestamp".into()))?;
            joined_onchain_at = Some(joined_at);
            player2_pubkey = Some(decoded.player2.to_string());
            MatchStatus::JoinedOnChain
        }
        DecodedGameState::Settled => {
            return Err(AppError::Conflict(
                "game is already settled on-chain; no finalization needed".into(),
            ))
        }
        DecodedGameState::Refunded => {
            return Err(AppError::Conflict(
                "game is already refunded on-chain; no finalization needed".into(),
            ))
        }
    };

    let winner_pubkey = match payload.outcome {
        ResultOutcome::Winner => {
            if decoded.state != DecodedGameState::Joined {
                return Err(AppError::Conflict(
                    "outcome=winner requires game state=Joined".into(),
                ));
            }

            let winner = payload.winner_pubkey.as_deref().unwrap_or("").trim();
            if winner.is_empty() {
                return Err(AppError::BadRequest(
                    "winner_pubkey is required when outcome=winner".into(),
                ));
            }
            let player2 = player2_pubkey
                .as_deref()
                .ok_or_else(|| AppError::Conflict("joined game is missing player2".into()))?;
            if winner != player1_pubkey && winner != player2 {
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

            if !matches!(
                decoded.state,
                DecodedGameState::Created | DecodedGameState::Joined
            ) {
                return Err(AppError::Conflict(
                    "outcome=broken requires game state=Created or Joined".into(),
                ));
            }
            None
        }
    };

    matches_db::upsert_match_from_chain(
        &state.pool,
        &matches_db::UpsertMatchFromChainParams {
            match_id: match_id_i64,
            program_id: &state.config.program_id,
            authority_pubkey: &authority_pubkey,
            game_pda,
            vault_pda: &expected_pdas.vault_pda,
            player1_pubkey: &player1_pubkey,
            player2_pubkey: player2_pubkey.as_deref(),
            entry_lamports: entry_lamports_i64,
            match_status: inferred_match_status,
            created_onchain_at,
            joined_onchain_at,
        },
    )
    .await?;

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

    Ok(Json(FinalizeResponse {
        match_id: match_id_i64.to_string(),
        match_status: persisted.match_status,
        finalization_action: persisted.chain_job_type,
        chain_job_status: persisted.chain_job_status,
    }))
}
