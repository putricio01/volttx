use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

use crate::models::enums::{ChainJobStatus, ChainJobType, MatchStatus, ResultOutcome};

#[derive(Debug, Deserialize)]
pub struct CreateMatchRequest {
    pub player1_pubkey: String,
    pub entry_lamports: String,
}

#[derive(Debug, Serialize)]
pub struct CreateMatchResponse {
    pub match_id: String,
    pub join_code: String,
    pub program_id: String,
    pub authority_pubkey: String,
    pub game_pda: String,
    pub vault_pda: String,
    pub entry_lamports: String,
    pub join_timeout_seconds: i64,
    pub settle_timeout_seconds: i64,
    pub match_status: MatchStatus,
}

#[derive(Debug, Serialize)]
pub struct MatchLookupByCodeResponse {
    pub match_id: String,
    pub join_code: String,
    pub game_pda: String,
    pub vault_pda: String,
    pub player1_pubkey: String,
    pub entry_lamports: String,
    pub match_status: MatchStatus,
    pub join_expires_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Deserialize)]
pub struct CreateConfirmRequest {
    pub create_tx_sig: String,
}

#[derive(Debug, Serialize)]
pub struct CreateConfirmResponse {
    pub match_id: String,
    pub verified: bool,
    pub match_status: MatchStatus,
    pub create_tx_sig: String,
    pub join_expires_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Deserialize)]
pub struct JoinConfirmRequest {
    pub join_tx_sig: String,
}

#[derive(Debug, Serialize)]
pub struct JoinConfirmResponse {
    pub match_id: String,
    pub verified: bool,
    pub match_status: MatchStatus,
    pub player2_pubkey: String,
    pub join_tx_sig: String,
    pub settle_expires_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Deserialize)]
pub struct ResultRequest {
    pub outcome: ResultOutcome,
    pub winner_pubkey: Option<String>,
    pub reason_code: String,
    pub reason_detail: Option<String>,
    pub idempotency_key: String,
}

#[derive(Debug, Serialize)]
pub struct ResultResponse {
    pub match_id: String,
    pub match_status: MatchStatus,
    pub finalization_action: ChainJobType,
    pub chain_job_status: ChainJobStatus,
}

#[derive(Debug, Serialize)]
pub struct MatchStatusResponse {
    pub match_id: String,
    pub join_code: String,
    pub program_id: String,
    pub authority_pubkey: String,
    pub game_pda: String,
    pub vault_pda: String,
    pub player1_pubkey: String,
    pub player2_pubkey: Option<String>,
    pub entry_lamports: String,
    pub pot_lamports: String,
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

#[derive(Debug, Deserialize)]
pub struct RetryFinalizationRequest {
    pub reason: String,
}

#[derive(Debug, Serialize)]
pub struct RetryFinalizationResponse {
    pub match_id: String,
    pub match_status: MatchStatus,
    pub chain_job_status: ChainJobStatus,
}

