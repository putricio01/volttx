use axum::{
    extract::{Path, State},
    http::StatusCode,
    response::IntoResponse,
    routing::{get, post},
    Json, Router,
};
use sqlx::Row;

use crate::{
    app_state::AppState,
    error::AppError,
    models::dto::{
        AcceptChallengeRequest, AcceptChallengeResponse, ChallengeInfo, ChallengeListResponse,
        ChallengeStatusResponse, RegisterChallengeRequest, RegisterChallengeResponse,
    },
};

pub fn router() -> Router<AppState> {
    Router::new()
        .route("/challenges", get(list_challenges).post(register_challenge))
        .route("/challenges/{game_pda}/accept", post(accept_challenge))
        .route("/challenges/{game_pda}/status", get(challenge_status))
}

/// GET /v1/challenges — list open challenges (status = created_on_chain)
async fn list_challenges(
    State(state): State<AppState>,
) -> Result<impl IntoResponse, AppError> {
    let rows = sqlx::query(
        r#"
        select game_pda, player1_pubkey, entry_lamports, match_id,
               extract(epoch from created_at)::bigint as created_at_epoch,
               match_status
        from matches
        where match_status = 'created_on_chain'
        order by created_at desc
        limit 50
        "#,
    )
    .fetch_all(&state.pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to list challenges: {e}")))?;

    let challenges: Vec<ChallengeInfo> = rows
        .into_iter()
        .map(|r| ChallengeInfo {
            game_pda: r.get("game_pda"),
            creator_pubkey: r.get("player1_pubkey"),
            entry_amount: r.get("entry_lamports"),
            match_id: r.get("match_id"),
            created_at: r.get::<Option<i64>, _>("created_at_epoch").unwrap_or(0),
            status: r.get("match_status"),
        })
        .collect();

    Ok(Json(ChallengeListResponse { challenges }))
}

/// POST /v1/challenges — register a new challenge after on-chain create_game.
/// Assigns a server immediately so the creator can connect right away.
async fn register_challenge(
    State(state): State<AppState>,
    Json(body): Json<RegisterChallengeRequest>,
) -> Result<impl IntoResponse, AppError> {
    if body.game_pda.is_empty() {
        return Err(AppError::BadRequest("game_pda is required".into()));
    }
    if body.creator_pubkey.is_empty() {
        return Err(AppError::BadRequest("creator_pubkey is required".into()));
    }
    if body.entry_amount == 0 {
        return Err(AppError::BadRequest("entry_amount must be > 0".into()));
    }
    if body.match_id == 0 {
        return Err(AppError::BadRequest("match_id must be > 0".into()));
    }

    let match_id = body.match_id as i64;
    let entry_lamports = body.entry_amount as i64;

    // Deterministic join code
    let join_code = crate::db::matches::join_code_from_match_id(match_id)?;

    // Find an idle server from the pool so the creator can connect immediately
    let server_row = sqlx::query(
        r#"
        select server_id, ip, port
        from server_pool
        where status = 'idle'
          and last_heartbeat_at > now() - interval '60 seconds'
        order by last_heartbeat_at desc
        limit 1
        for update skip locked
        "#,
    )
    .fetch_optional(&state.pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to find idle server: {e}")))?
    .ok_or_else(|| {
        AppError::Internal("no idle servers available — try again shortly".into())
    })?;

    let server_id: String = server_row.get("server_id");
    let server_ip: String = server_row.get("ip");
    let server_port: i32 = server_row.get("port");

    // Mark server as busy
    sqlx::query(
        "update server_pool set status = 'busy', assigned_match_id = $1 where server_id = $2",
    )
    .bind(match_id)
    .bind(&server_id)
    .execute(&state.pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to assign server: {e}")))?;

    // Insert match with assigned server
    sqlx::query(
        r#"
        insert into matches (
          match_id, join_code, program_id, authority_pubkey,
          game_pda, vault_pda, player1_pubkey,
          entry_lamports, match_status, assigned_server_id, created_onchain_at
        )
        values ($1, $2, $3, $4, $5, '', $6, $7, 'created_on_chain', $8, now())
        on conflict (match_id) do nothing
        "#,
    )
    .bind(match_id)
    .bind(&join_code)
    .bind(&state.config.program_id)
    .bind(&state.config.authority_pubkey)
    .bind(&body.game_pda)
    .bind(&body.creator_pubkey)
    .bind(entry_lamports)
    .bind(&server_id)
    .execute(&state.pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to register challenge: {e}")))?;

    Ok((StatusCode::CREATED, Json(RegisterChallengeResponse {
        ok: true,
        server_ip,
        server_port,
    })))
}

/// POST /v1/challenges/{game_pda}/accept — accept a challenge.
/// Server was already assigned on creation; returns the same server info.
async fn accept_challenge(
    State(state): State<AppState>,
    Path(game_pda): Path<String>,
    Json(body): Json<AcceptChallengeRequest>,
) -> Result<impl IntoResponse, AppError> {
    if body.acceptor_pubkey.is_empty() {
        return Err(AppError::BadRequest("acceptor_pubkey is required".into()));
    }

    // Lookup the challenge and its already-assigned server
    let match_row = sqlx::query(
        r#"
        select m.match_id, sp.ip as server_ip, sp.port as server_port
        from matches m
        join server_pool sp on sp.server_id = m.assigned_server_id
        where m.game_pda = $1 and m.match_status = 'created_on_chain'
        "#,
    )
    .bind(&game_pda)
    .fetch_optional(&state.pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to lookup challenge: {e}")))?
    .ok_or_else(|| AppError::BadRequest("challenge not found or already accepted".into()))?;

    let match_id: i64 = match_row.get("match_id");
    let server_ip: String = match_row.get("server_ip");
    let server_port: i32 = match_row.get("server_port");

    // Update match: set acceptor, status → joined_on_chain
    sqlx::query(
        r#"
        update matches
        set acceptor_pubkey = $1,
            player2_pubkey = $1,
            match_status = 'joined_on_chain',
            joined_onchain_at = now(),
            updated_at = now()
        where match_id = $2
        "#,
    )
    .bind(&body.acceptor_pubkey)
    .bind(match_id)
    .execute(&state.pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to update match: {e}")))?;

    Ok(Json(AcceptChallengeResponse {
        server_ip,
        server_port,
        status: "matched".to_string(),
    }))
}

/// GET /v1/challenges/{game_pda}/status — poll challenge status (for creator)
async fn challenge_status(
    State(state): State<AppState>,
    Path(game_pda): Path<String>,
) -> Result<impl IntoResponse, AppError> {
    let row = sqlx::query(
        r#"
        select m.match_status, sp.ip as server_ip, sp.port as server_port
        from matches m
        left join server_pool sp on sp.server_id = m.assigned_server_id
        where m.game_pda = $1
        "#,
    )
    .bind(&game_pda)
    .fetch_optional(&state.pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to get challenge status: {e}")))?
    .ok_or_else(|| AppError::BadRequest("challenge not found".into()))?;

    Ok(Json(ChallengeStatusResponse {
        status: row.get("match_status"),
        server_ip: row.get("server_ip"),
        server_port: row.get("server_port"),
    }))
}
