use std::time::Duration;

use crate::app_state::AppState;

pub fn spawn(state: AppState) {
    tokio::spawn(async move {
        let interval = Duration::from_millis(state.config.timeout_watcher_poll_ms);
        loop {
            // TODO: queue `force_refund` for expired CreatedOnChain matches with no join.
            tracing::trace!("timeout watcher tick");
            tokio::time::sleep(interval).await;
        }
    });
}

