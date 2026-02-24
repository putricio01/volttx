pub mod finalizer;
pub mod timeout_watcher;

use crate::app_state::AppState;

pub fn spawn_workers(state: AppState) {
    finalizer::spawn(state.clone());
    timeout_watcher::spawn(state);
}
