pub mod finalizer;

use crate::app_state::AppState;

pub fn spawn_workers(state: AppState) {
    finalizer::spawn(state);
}
