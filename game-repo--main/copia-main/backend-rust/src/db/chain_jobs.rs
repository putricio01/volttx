//! DB helpers for `chain_jobs`.
//!
//! This module will own:
//! - enqueue finalization job
//! - lock next due job (`FOR UPDATE SKIP LOCKED`)
//! - mark submitted/retrying/confirmed/failed

#[allow(dead_code)]
pub struct ChainJobRow;
