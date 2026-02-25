use axum::http::HeaderMap;
use chrono::Utc;
use hmac::{Hmac, Mac};
use sha2::Sha256;

use crate::{app_state::AppState, db::used_nonces, error::AppError};

const HEADER_TIMESTAMP: &str = "X-Timestamp";
const HEADER_NONCE: &str = "X-Nonce";
const HEADER_SIGNATURE: &str = "X-Signature";
const MAX_CLOCK_SKEW_SECONDS: i64 = 300;
const MAX_NONCE_LEN: usize = 128;

type HmacSha256 = Hmac<Sha256>;

pub async fn verify_internal_hmac(
    state: &AppState,
    headers: &HeaderMap,
    raw_body: &[u8],
) -> Result<(), AppError> {
    if state.config.internal_hmac_secret.is_empty() {
        return Err(AppError::Unauthorized);
    }

    let timestamp_raw = header_value(headers, HEADER_TIMESTAMP)?;
    let nonce = header_value(headers, HEADER_NONCE)?.trim();
    let signature_raw = header_value(headers, HEADER_SIGNATURE)?;

    if nonce.is_empty() || nonce.len() > MAX_NONCE_LEN {
        return Err(AppError::Unauthorized);
    }

    let timestamp = parse_timestamp(timestamp_raw)?;
    let now = Utc::now().timestamp();
    if (now - timestamp).abs() > MAX_CLOCK_SKEW_SECONDS {
        return Err(AppError::Unauthorized);
    }

    let provided_sig = parse_signature_hex(signature_raw)?;

    let mut mac = HmacSha256::new_from_slice(state.config.internal_hmac_secret.as_bytes())
        .map_err(|_| AppError::Internal("failed to initialize HMAC".into()))?;
    mac.update(timestamp_raw.trim().as_bytes());
    mac.update(b".");
    mac.update(nonce.as_bytes());
    mac.update(b".");
    mac.update(raw_body);
    mac.verify_slice(&provided_sig)
        .map_err(|_| AppError::Unauthorized)?;

    let inserted = used_nonces::insert_nonce_if_unused(&state.pool, nonce).await?;
    if !inserted {
        return Err(AppError::Unauthorized);
    }

    Ok(())
}

fn header_value<'a>(headers: &'a HeaderMap, name: &str) -> Result<&'a str, AppError> {
    let value = headers.get(name).ok_or(AppError::Unauthorized)?;
    value.to_str().map_err(|_| AppError::Unauthorized)
}

fn parse_timestamp(raw: &str) -> Result<i64, AppError> {
    raw.trim()
        .parse::<i64>()
        .map_err(|_| AppError::Unauthorized)
}

fn parse_signature_hex(raw: &str) -> Result<Vec<u8>, AppError> {
    let trimmed = raw.trim();
    let hex_str = trimmed
        .strip_prefix("sha256=")
        .or_else(|| trimmed.strip_prefix("SHA256="))
        .unwrap_or(trimmed);

    if hex_str.is_empty() {
        return Err(AppError::Unauthorized);
    }

    hex::decode(hex_str).map_err(|_| AppError::Unauthorized)
}
