//! DB helpers for `matches`.
//!
//! Implement these with `sqlx::query!` / `sqlx::query_as!` once the route layer is ready.
//! Keeping this module separate prevents API handlers from growing into SQL blobs.

#[allow(dead_code)]
pub struct MatchRow;

