pub mod admin;
pub mod matches;

use axum::Router;

use crate::app_state::AppState;

pub fn router() -> Router<AppState> {
    Router::new()
        .nest("/matches", matches::router())
        .nest("/admin", admin::router())
}

