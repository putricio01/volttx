use std::time::Duration;

use crate::app_state::AppState;

pub fn spawn(state: AppState) {
    tokio::spawn(async move {
        let interval = Duration::from_millis(state.config.finalizer_poll_ms);
        loop {
            // TODO: load due chain_jobs, verify on-chain state, submit/confirm settle/refund tx.
            tracing::trace!("finalizer tick");
            tokio::time::sleep(interval).await;
        }
    });
}

