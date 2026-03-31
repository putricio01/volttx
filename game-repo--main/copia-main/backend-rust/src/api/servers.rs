use axum::{
    body::Bytes,
    extract::{Path, State},
    http::{HeaderMap, StatusCode},
    response::IntoResponse,
    routing::{post, put},
    Json, Router,
};

use crate::{
    api::internal_auth::verify_internal_hmac,
    app_state::AppState,
    error::AppError,
    models::dto::{HeartbeatRequest, RegisterServerRequest},
};

pub fn router() -> Router<AppState> {
    Router::new()
        .route("/servers/register", post(register_server))
        .route("/servers/{server_id}/heartbeat", put(heartbeat))
}

/// POST /v1/servers/register — server instance registers in pool (HMAC-protected)
async fn register_server(
    State(state): State<AppState>,
    headers: HeaderMap,
    body: Bytes,
) -> Result<impl IntoResponse, AppError> {
    verify_internal_hmac(&state, &headers, body.as_ref()).await?;

    let req: RegisterServerRequest = serde_json::from_slice(body.as_ref())
        .map_err(|e| AppError::BadRequest(format!("invalid JSON: {e}")))?;

    if req.server_id.is_empty() || req.ip.is_empty() || req.port <= 0 {
        return Err(AppError::BadRequest(
            "server_id, ip, and port are required".into(),
        ));
    }

    sqlx::query(
        r#"
        insert into server_pool (server_id, ip, port, status, last_heartbeat_at)
        values ($1, $2, $3, $4, now())
        on conflict (server_id) do update
        set ip = excluded.ip,
            port = excluded.port,
            status = excluded.status,
            last_heartbeat_at = now()
        "#,
    )
    .bind(&req.server_id)
    .bind(&req.ip)
    .bind(req.port)
    .bind(&req.status)
    .execute(&state.pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to register server: {e}")))?;

    tracing::info!(
        server_id = %req.server_id,
        ip = %req.ip,
        port = req.port,
        "server registered in pool"
    );

    Ok((StatusCode::OK, Json(serde_json::json!({"ok": true}))))
}

/// PUT /v1/servers/{server_id}/heartbeat — server heartbeat (HMAC-protected)
async fn heartbeat(
    State(state): State<AppState>,
    Path(server_id): Path<String>,
    headers: HeaderMap,
    body: Bytes,
) -> Result<impl IntoResponse, AppError> {
    verify_internal_hmac(&state, &headers, body.as_ref()).await?;

    let req: HeartbeatRequest = serde_json::from_slice(body.as_ref())
        .map_err(|e| AppError::BadRequest(format!("invalid JSON: {e}")))?;

    sqlx::query(
        r#"
        update server_pool
        set status = $1, last_heartbeat_at = now()
        where server_id = $2
        "#,
    )
    .bind(&req.status)
    .bind(&server_id)
    .execute(&state.pool)
    .await
    .map_err(|e| AppError::Internal(format!("failed to update heartbeat: {e}")))?;

    Ok((StatusCode::OK, Json(serde_json::json!({"ok": true}))))
}
