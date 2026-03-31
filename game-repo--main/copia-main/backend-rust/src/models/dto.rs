use serde::{Deserialize, Serialize};

use crate::models::enums::{ChainJobStatus, ChainJobType, MatchStatus, ResultOutcome};

// ── Finalize ────────────────────────────────────────────

#[derive(Debug, Deserialize)]
pub struct FinalizeRequest {
    pub game_pda: String,
    pub outcome: ResultOutcome,
    pub winner_pubkey: Option<String>,
    pub reason_code: String,
    pub reason_detail: Option<String>,
    pub idempotency_key: String,
}

#[derive(Debug, Serialize)]
pub struct FinalizeResponse {
    pub match_id: String,
    pub match_status: MatchStatus,
    pub finalization_action: ChainJobType,
    pub chain_job_status: ChainJobStatus,
}

// ── Challenges ──────────────────────────────────────────

#[derive(Debug, Deserialize)]
pub struct RegisterChallengeRequest {
    pub game_pda: String,
    pub creator_pubkey: String,
    pub entry_amount: u64,
    pub match_id: u64,
}

#[derive(Debug, Serialize)]
pub struct ChallengeInfo {
    pub game_pda: String,
    pub creator_pubkey: String,
    pub entry_amount: i64,
    pub match_id: i64,
    pub created_at: i64,
    pub status: String,
}

#[derive(Debug, Serialize)]
pub struct ChallengeListResponse {
    pub challenges: Vec<ChallengeInfo>,
}

#[derive(Debug, Serialize)]
pub struct RegisterChallengeResponse {
    pub ok: bool,
    pub server_ip: String,
    pub server_port: i32,
}

#[derive(Debug, Deserialize)]
pub struct AcceptChallengeRequest {
    pub acceptor_pubkey: String,
}

#[derive(Debug, Serialize)]
pub struct AcceptChallengeResponse {
    pub server_ip: String,
    pub server_port: i32,
    pub status: String,
}

#[derive(Debug, Serialize)]
pub struct ChallengeStatusResponse {
    pub status: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub server_ip: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub server_port: Option<i32>,
}

// ── Server Pool ─────────────────────────────────────────

#[derive(Debug, Deserialize)]
pub struct RegisterServerRequest {
    pub server_id: String,
    pub ip: String,
    pub port: i32,
    pub status: String,
}

#[derive(Debug, Deserialize)]
pub struct HeartbeatRequest {
    pub status: String,
}
