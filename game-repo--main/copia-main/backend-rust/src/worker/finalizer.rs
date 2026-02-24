use std::{str::FromStr, time::Duration};

use anyhow::{anyhow, bail, Context, Result};
use sha2::{Digest, Sha256};
use solana_client::nonblocking::rpc_client::RpcClient;
use solana_sdk::{
    hash::Hash,
    instruction::{AccountMeta, Instruction},
    pubkey::Pubkey,
    signature::{read_keypair_file, Keypair, Signature, Signer},
    system_program,
    transaction::Transaction,
};

use crate::{
    app_state::AppState,
    db::chain_jobs as chain_jobs_db,
    models::enums::{ChainJobType, MatchStatus},
    solana::{
        client::fetch_and_decode_game_account_with_client,
        game_account::{DecodedGameAccount, DecodedGameState},
    },
};

const MAX_FINALIZER_ATTEMPTS: i32 = 10;
const CONFIRM_POLL_INTERVAL_MS: u64 = 500;
const CONFIRM_POLL_ATTEMPTS: usize = 40;
const MAX_BACKOFF_SECONDS: i64 = 60;

pub fn spawn(state: AppState) {
    tokio::spawn(async move {
        let idle_interval = Duration::from_millis(state.config.finalizer_poll_ms);

        let program_id = match Pubkey::from_str(&state.config.program_id) {
            Ok(v) => v,
            Err(e) => {
                tracing::error!("finalizer disabled: invalid PROGRAM_ID: {}", e);
                return;
            }
        };

        let authority = match load_authority_keypair(&state) {
            Ok(kp) => kp,
            Err(e) => {
                tracing::error!("finalizer disabled: failed to load authority keypair: {e:#}");
                return;
            }
        };

        if authority.pubkey().to_string() != state.config.authority_pubkey {
            tracing::error!(
                "finalizer disabled: authority keypair pubkey {} does not match AUTHORITY_PUBKEY {}",
                authority.pubkey(),
                state.config.authority_pubkey
            );
            return;
        }

        let rpc = RpcClient::new(state.config.solana_rpc_url.clone());
        tracing::info!("finalizer worker started");

        loop {
            match process_one_job(&state, &rpc, &program_id, &authority).await {
                Ok(true) => {}
                Ok(false) => tokio::time::sleep(idle_interval).await,
                Err(e) => {
                    tracing::error!("finalizer loop error: {e:#}");
                    tokio::time::sleep(idle_interval).await;
                }
            }
        }
    });
}

async fn process_one_job(
    state: &AppState,
    rpc: &RpcClient,
    program_id: &Pubkey,
    authority: &Keypair,
) -> Result<bool> {
    let Some(job) = chain_jobs_db::claim_next_due_finalizer_job(&state.pool).await? else {
        tracing::trace!("finalizer idle");
        return Ok(false);
    };

    tracing::info!(
        match_id = job.match_id,
        job_type = ?job.job_type,
        job_status = ?job.chain_job_status,
        attempt_count = job.attempt_count,
        "processing chain job"
    );

    let outcome = process_claimed_job(state, rpc, program_id, authority, &job).await;
    match outcome {
        Ok(()) => {}
        Err(e) => {
            let error_text = format!("{e:#}");
            // Unexpected processing failures (decode/build/DB) should eventually trip max attempts.
            let increment_attempt = true;
            schedule_retry_or_fail(state, &job, &error_text, increment_attempt).await?;
        }
    }

    Ok(true)
}

async fn process_claimed_job(
    state: &AppState,
    rpc: &RpcClient,
    program_id: &Pubkey,
    authority: &Keypair,
    job: &chain_jobs_db::ClaimedFinalizerJob,
) -> Result<()> {
    let decoded =
        fetch_and_decode_game_account_with_client(rpc, &state.config.program_id, &job.game_pda)
            .await
            .with_context(|| {
                format!(
                    "failed to fetch/decode game account for match {}",
                    job.match_id
                )
            })?;

    if decoded.authority != authority.pubkey() {
        chain_jobs_db::mark_job_failed(
            &state.pool,
            job.match_id,
            job.lock_token,
            "on-chain game.authority does not match backend authority keypair",
            false,
        )
        .await?;
        return Ok(());
    }

    match (job.job_type, decoded.state) {
        (ChainJobType::Settle, DecodedGameState::Settled) => {
            chain_jobs_db::mark_job_confirmed_and_finalize_match(
                &state.pool,
                job.match_id,
                job.lock_token,
                job.last_tx_sig.as_deref(),
                MatchStatus::Settled,
            )
            .await?;
            return Ok(());
        }
        (ChainJobType::ForceRefund, DecodedGameState::Refunded) => {
            chain_jobs_db::mark_job_confirmed_and_finalize_match(
                &state.pool,
                job.match_id,
                job.lock_token,
                job.last_tx_sig.as_deref(),
                MatchStatus::Refunded,
            )
            .await?;
            return Ok(());
        }
        (ChainJobType::Settle, DecodedGameState::Refunded) => {
            chain_jobs_db::mark_job_failed(
                &state.pool,
                job.match_id,
                job.lock_token,
                "job requests settle but on-chain game is already refunded",
                false,
            )
            .await?;
            return Ok(());
        }
        (ChainJobType::ForceRefund, DecodedGameState::Settled) => {
            chain_jobs_db::mark_job_failed(
                &state.pool,
                job.match_id,
                job.lock_token,
                "job requests force_refund but on-chain game is already settled",
                false,
            )
            .await?;
            return Ok(());
        }
        _ => {}
    }

    let (instruction, final_match_status) =
        build_finalization_instruction(*program_id, authority.pubkey(), &decoded, job)
            .with_context(|| {
                format!(
                    "failed to build finalization instruction for match {}",
                    job.match_id
                )
            })?;

    let signature = match send_instruction(rpc, authority, instruction).await {
        Ok(sig) => sig,
        Err(e) => {
            schedule_retry_or_fail(state, job, &format!("{e:#}"), true).await?;
            return Ok(());
        }
    };

    let sig_text = signature.to_string();
    if let Err(e) =
        chain_jobs_db::mark_job_submitted(&state.pool, job.match_id, job.lock_token, &sig_text)
            .await
    {
        tracing::error!(
            match_id = job.match_id,
            signature = %sig_text,
            "failed to persist submitted tx: {e}"
        );
        let _ = chain_jobs_db::clear_job_lock(&state.pool, job.match_id, job.lock_token).await;
        return Err(anyhow!(e.to_string()));
    }

    if let Err(e) = wait_for_signature_confirmation(rpc, &signature).await {
        schedule_retry_or_fail(state, job, &format!("{e:#}"), false).await?;
        return Ok(());
    }

    chain_jobs_db::mark_job_confirmed_and_finalize_match(
        &state.pool,
        job.match_id,
        job.lock_token,
        Some(&sig_text),
        final_match_status,
    )
    .await?;

    tracing::info!(
        match_id = job.match_id,
        final_status = ?final_match_status,
        signature = %sig_text,
        "finalizer completed chain job"
    );
    Ok(())
}

async fn schedule_retry_or_fail(
    state: &AppState,
    job: &chain_jobs_db::ClaimedFinalizerJob,
    error_message: &str,
    increment_attempt_count: bool,
) -> Result<()> {
    let projected_attempts = job.attempt_count + i32::from(increment_attempt_count);

    if projected_attempts >= MAX_FINALIZER_ATTEMPTS {
        chain_jobs_db::mark_job_failed(
            &state.pool,
            job.match_id,
            job.lock_token,
            error_message,
            increment_attempt_count,
        )
        .await?;
        tracing::error!(
            match_id = job.match_id,
            attempts = projected_attempts,
            "chain job marked failed: {}",
            error_message
        );
        return Ok(());
    }

    let backoff_seconds = retry_backoff_seconds(projected_attempts);
    chain_jobs_db::mark_job_retrying(
        &state.pool,
        job.match_id,
        job.lock_token,
        error_message,
        backoff_seconds,
        increment_attempt_count,
    )
    .await?;
    tracing::warn!(
        match_id = job.match_id,
        attempts = projected_attempts,
        backoff_seconds,
        "chain job scheduled for retry: {}",
        error_message
    );
    Ok(())
}

fn retry_backoff_seconds(attempts: i32) -> i64 {
    let exp = attempts.clamp(1, 6) as u32;
    let secs = 1_i64.checked_shl(exp).unwrap_or(MAX_BACKOFF_SECONDS);
    secs.min(MAX_BACKOFF_SECONDS)
}

fn load_authority_keypair(state: &AppState) -> Result<Keypair> {
    read_keypair_file(&state.config.authority_keypair_path)
        .map_err(|e| anyhow!("read_keypair_file failed: {e}"))
}

fn build_finalization_instruction(
    program_id: Pubkey,
    authority_pubkey: Pubkey,
    game: &DecodedGameAccount,
    job: &chain_jobs_db::ClaimedFinalizerJob,
) -> Result<(Instruction, MatchStatus)> {
    let game_pda = Pubkey::from_str(&job.game_pda).context("invalid game_pda in DB")?;
    let vault_pda = Pubkey::from_str(&job.vault_pda).context("invalid vault_pda in DB")?;

    match job.job_type {
        ChainJobType::Settle => {
            if game.state != DecodedGameState::Joined {
                bail!(
                    "cannot call settle_game when on-chain state is {:?}",
                    game.state
                );
            }
            let winner_str = job
                .winner_pubkey
                .as_deref()
                .ok_or_else(|| anyhow!("settle job missing winner_pubkey"))?;
            let winner =
                Pubkey::from_str(winner_str).context("invalid winner_pubkey in chain job")?;
            if winner != game.player1 && winner != game.player2 {
                bail!("winner_pubkey in chain job does not match on-chain players");
            }

            let mut data = anchor_ix_discriminator("settle_game").to_vec();
            data.extend_from_slice(winner.as_ref());

            let ix = Instruction {
                program_id,
                accounts: vec![
                    AccountMeta::new(game_pda, false),
                    AccountMeta::new(vault_pda, false),
                    AccountMeta::new(winner, false),
                    AccountMeta::new_readonly(authority_pubkey, true),
                    AccountMeta::new_readonly(system_program::id(), false),
                ],
                data,
            };
            Ok((ix, MatchStatus::Settled))
        }
        ChainJobType::ForceRefund => {
            match game.state {
                DecodedGameState::Created | DecodedGameState::Joined => {}
                _ => bail!(
                    "cannot call force_refund when on-chain state is {:?}",
                    game.state
                ),
            }

            // In Created state player2 may still be default; passing player1 keeps the account loaded safely.
            let player2_for_accounts =
                if game.state == DecodedGameState::Created && game.player2 == Pubkey::default() {
                    game.player1
                } else {
                    game.player2
                };

            let ix = Instruction {
                program_id,
                accounts: vec![
                    AccountMeta::new(game_pda, false),
                    AccountMeta::new(vault_pda, false),
                    AccountMeta::new(game.player1, false),
                    AccountMeta::new(player2_for_accounts, false),
                    AccountMeta::new_readonly(authority_pubkey, true),
                    AccountMeta::new_readonly(system_program::id(), false),
                ],
                data: anchor_ix_discriminator("force_refund").to_vec(),
            };
            Ok((ix, MatchStatus::Refunded))
        }
    }
}

async fn send_instruction(
    rpc: &RpcClient,
    authority: &Keypair,
    ix: Instruction,
) -> Result<Signature> {
    let recent_blockhash: Hash = rpc
        .get_latest_blockhash()
        .await
        .context("failed to fetch latest blockhash")?;

    let tx = Transaction::new_signed_with_payer(
        &[ix],
        Some(&authority.pubkey()),
        &[authority],
        recent_blockhash,
    );

    rpc.send_transaction(&tx)
        .await
        .context("failed to send transaction")
}

async fn wait_for_signature_confirmation(rpc: &RpcClient, signature: &Signature) -> Result<()> {
    for _ in 0..CONFIRM_POLL_ATTEMPTS {
        let statuses = rpc
            .get_signature_statuses(&[*signature])
            .await
            .context("failed to fetch signature status")?;

        if let Some(status_opt) = statuses.value.first() {
            if let Some(status) = status_opt {
                if let Some(err) = &status.err {
                    bail!("transaction failed on-chain: {err:?}");
                }
                return Ok(());
            }
        }

        tokio::time::sleep(Duration::from_millis(CONFIRM_POLL_INTERVAL_MS)).await;
    }

    bail!("timed out waiting for transaction confirmation");
}

fn anchor_ix_discriminator(method_name: &str) -> [u8; 8] {
    let mut hasher = Sha256::new();
    hasher.update(format!("global:{method_name}").as_bytes());
    let hash = hasher.finalize();
    let mut out = [0u8; 8];
    out.copy_from_slice(&hash[..8]);
    out
}
