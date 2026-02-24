//! Manual decoder for the Anchor `Game` account.
//!
//! This keeps MVP integration simple without importing the program repo crate yet.

use anyhow::{bail, ensure, Result};
use sha2::{Digest, Sha256};
use solana_sdk::pubkey::Pubkey;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DecodedGameState {
    Created,
    Joined,
    Settled,
    Refunded,
}

#[derive(Debug, Clone)]
pub struct DecodedGameAccount {
    pub player1: Pubkey,
    pub player2: Pubkey,
    pub entry_amount: u64,
    pub authority: Pubkey,
    pub match_id: u64,
    pub state: DecodedGameState,
    pub created_at: i64,
    pub joined_at: i64,
    pub bump: u8,
    pub vault_bump: u8,
}

pub fn decode_game_account(data: &[u8]) -> Result<DecodedGameAccount> {
    const BODY_LEN: usize = 32 + 32 + 8 + 32 + 8 + 1 + 8 + 8 + 1 + 1;
    ensure!(
        data.len() >= 8 + BODY_LEN,
        "game account data too short: {} bytes",
        data.len()
    );

    let expected_discriminator = game_account_discriminator();
    ensure!(
        data[..8] == expected_discriminator,
        "invalid Game discriminator"
    );

    let mut i = 8usize;
    let player1 = read_pubkey(data, &mut i)?;
    let player2 = read_pubkey(data, &mut i)?;
    let entry_amount = read_u64(data, &mut i)?;
    let authority = read_pubkey(data, &mut i)?;
    let match_id = read_u64(data, &mut i)?;
    let state = match read_u8(data, &mut i)? {
        0 => DecodedGameState::Created,
        1 => DecodedGameState::Joined,
        2 => DecodedGameState::Settled,
        3 => DecodedGameState::Refunded,
        other => bail!("invalid GameState variant: {other}"),
    };
    let created_at = read_i64(data, &mut i)?;
    let joined_at = read_i64(data, &mut i)?;
    let bump = read_u8(data, &mut i)?;
    let vault_bump = read_u8(data, &mut i)?;

    Ok(DecodedGameAccount {
        player1,
        player2,
        entry_amount,
        authority,
        match_id,
        state,
        created_at,
        joined_at,
        bump,
        vault_bump,
    })
}

fn game_account_discriminator() -> [u8; 8] {
    let mut hasher = Sha256::new();
    hasher.update(b"account:Game");
    let hash = hasher.finalize();
    let mut out = [0u8; 8];
    out.copy_from_slice(&hash[..8]);
    out
}

fn read_pubkey(data: &[u8], i: &mut usize) -> Result<Pubkey> {
    let bytes = read_fixed::<32>(data, i)?;
    Ok(Pubkey::new_from_array(bytes))
}

fn read_u64(data: &[u8], i: &mut usize) -> Result<u64> {
    Ok(u64::from_le_bytes(read_fixed::<8>(data, i)?))
}

fn read_i64(data: &[u8], i: &mut usize) -> Result<i64> {
    Ok(i64::from_le_bytes(read_fixed::<8>(data, i)?))
}

fn read_u8(data: &[u8], i: &mut usize) -> Result<u8> {
    let b = *data
        .get(*i)
        .ok_or_else(|| anyhow::anyhow!("read past end of account data"))?;
    *i += 1;
    Ok(b)
}

fn read_fixed<const N: usize>(data: &[u8], i: &mut usize) -> Result<[u8; N]> {
    let end = *i + N;
    let slice = data
        .get(*i..end)
        .ok_or_else(|| anyhow::anyhow!("read past end of account data"))?;
    let mut out = [0u8; N];
    out.copy_from_slice(slice);
    *i = end;
    Ok(out)
}
