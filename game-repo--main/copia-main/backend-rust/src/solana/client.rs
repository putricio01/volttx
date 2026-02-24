//! Solana integration helpers used by the API/worker.

use std::str::FromStr;

use anyhow::{Context, Result};
use solana_client::nonblocking::rpc_client::RpcClient;
use solana_sdk::pubkey::Pubkey;

use crate::solana::game_account::{decode_game_account, DecodedGameAccount};

pub async fn fetch_and_decode_game_account(
    rpc_url: &str,
    program_id: &str,
    game_pda: &str,
) -> Result<DecodedGameAccount> {
    let client = RpcClient::new(rpc_url.to_string());
    let expected_program_id = Pubkey::from_str(program_id).context("invalid PROGRAM_ID")?;
    let game_pubkey = Pubkey::from_str(game_pda).context("invalid game_pda")?;

    let account = client
        .get_account(&game_pubkey)
        .await
        .with_context(|| format!("failed to fetch game account {}", game_pubkey))?;

    if account.owner != expected_program_id {
        anyhow::bail!(
            "unexpected game account owner: expected {}, got {}",
            expected_program_id,
            account.owner
        );
    }

    decode_game_account(&account.data)
}
