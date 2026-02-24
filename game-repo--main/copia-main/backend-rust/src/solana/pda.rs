//! PDA derivation for the on-chain game program.

use std::str::FromStr;

use anyhow::{bail, Context, Result};
use solana_sdk::pubkey::Pubkey;

const GAME_SEED: &[u8] = b"game";
const VAULT_SEED: &[u8] = b"vault";

#[derive(Debug, Clone)]
pub struct MatchPdas {
    pub game_pda: String,
    pub vault_pda: String,
}

pub fn derive_match_pdas(
    program_id: &str,
    authority_pubkey: &str,
    player1_pubkey: &str,
    match_id: i64,
) -> Result<MatchPdas> {
    if match_id < 0 {
        bail!("match_id must be non-negative");
    }

    let program_id = Pubkey::from_str(program_id).context("invalid PROGRAM_ID")?;
    let authority = Pubkey::from_str(authority_pubkey).context("invalid AUTHORITY_PUBKEY")?;
    let player1 = Pubkey::from_str(player1_pubkey).context("invalid player1_pubkey")?;
    let match_id_u64 = match_id as u64;
    let match_id_bytes = match_id_u64.to_le_bytes();

    let (game_pda, _) = Pubkey::find_program_address(
        &[
            GAME_SEED,
            player1.as_ref(),
            authority.as_ref(),
            &match_id_bytes,
        ],
        &program_id,
    );
    let (vault_pda, _) =
        Pubkey::find_program_address(&[VAULT_SEED, game_pda.as_ref()], &program_id);

    Ok(MatchPdas {
        game_pda: game_pda.to_string(),
        vault_pda: vault_pda.to_string(),
    })
}
