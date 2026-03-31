pub mod internal_auth;
pub mod matches;
pub mod challenges;
pub mod servers;

use axum::Router;

use crate::app_state::AppState;

pub fn router() -> Router<AppState> {
    Router::new()
        .merge(matches::router())
        .merge(challenges::router())
        .merge(servers::router())
}
