use std::time::Duration;

use anyhow::Result;

use crate::{app_state::AppState, db::chain_jobs as chain_jobs_db};

const MAX_ENQUEUES_PER_TICK: usize = 25;

pub fn spawn(state: AppState) {
    tokio::spawn(async move {
        let interval = Duration::from_millis(state.config.timeout_watcher_poll_ms);
        tracing::info!("timeout watcher started");
        loop {
            if let Err(e) = process_tick(&state).await {
                tracing::error!("timeout watcher tick failed: {e:#}");
            }
            tokio::time::sleep(interval).await;
        }
    });
}

async fn process_tick(state: &AppState) -> Result<()> {
    let mut enqueued = 0usize;

    for _ in 0..MAX_ENQUEUES_PER_TICK {
        let Some(queued) =
            chain_jobs_db::enqueue_next_expired_join_timeout_force_refund(&state.pool).await?
        else {
            break;
        };

        enqueued += 1;
        tracing::info!(
            match_id = queued.match_id,
            chain_job_status = ?queued.chain_job_status,
            "queued join-timeout force_refund"
        );
    }

    if enqueued == 0 {
        tracing::trace!("timeout watcher idle");
    } else {
        tracing::debug!(count = enqueued, "timeout watcher queued expired matches");
    }

    Ok(())
}
