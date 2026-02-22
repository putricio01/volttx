using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Solana.Unity.Programs;
using Solana.Unity.Rpc.Builders;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Types;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using SolanaGame.Program;
using UnityEngine;

public class ttservise : MonoBehaviour
{
    public static readonly PublicKey ProgramId = new PublicKey("3abFWCLDDyA2jHfnGLQUTX6W9jddXSMHt9jtyc6Xjfjc");

    private static readonly byte[] GameSeedBytes = Encoding.UTF8.GetBytes(GameProgramInstructions.GameSeed);
    private static readonly byte[] VaultSeedBytes = Encoding.UTF8.GetBytes(GameProgramInstructions.VaultSeed);

    public string LastGameAddress { get; private set; }
    public string LastVaultAddress { get; private set; }
    public string LastSignature { get; private set; }

    private void Awake()
    {
        if (Web3.Instance == null)
        {
            Debug.LogError("Web3 instance is not initialized.");
        }
    }

    public async Task<bool> CreateGameTransaction(ulong entryAmount)
    {
        if (entryAmount == 0)
        {
            Debug.LogError("Entry amount must be greater than zero.");
            return false;
        }

        if (!TryGetActiveAccount(out var player1))
            return false;

        var authority = player1.PublicKey;
        if (!TryDeriveGamePda(player1.PublicKey, authority, out var gamePda))
        {
            Debug.LogError("Failed to derive game PDA.");
            return false;
        }

        if (!TryDeriveVaultPda(gamePda, out var vaultPda))
        {
            Debug.LogError("Failed to derive vault PDA.");
            return false;
        }

        LastGameAddress = gamePda.Key;
        LastVaultAddress = vaultPda.Key;

        var accounts = new CreateGameAccounts
        {
            Player1 = player1.PublicKey,
            Authority = authority,
            Game = gamePda,
            Vault = vaultPda,
            SystemProgram = SystemProgram.ProgramIdKey
        };

        var builder = new TransactionBuilder()
            .SetFeePayer(player1)
            .AddInstruction(GameProgramInstructions.CreateGame(accounts, entryAmount, ProgramId));

        return await SignAndSendAsync(builder, new List<Account> { player1 }, "create_game");
    }

    public async Task<bool> JoinGameTransaction(string gameAddress)
    {
        if (!TryGetActiveAccount(out var player2))
            return false;

        if (!TryParsePublicKey(gameAddress, "gameAddress", out var gamePda))
            return false;

        if (!TryDeriveVaultPda(gamePda, out var vaultPda))
        {
            Debug.LogError("Failed to derive vault PDA.");
            return false;
        }

        var accounts = new JoinGameAccounts
        {
            Player2 = player2.PublicKey,
            Game = gamePda,
            Vault = vaultPda,
            SystemProgram = SystemProgram.ProgramIdKey
        };

        var builder = new TransactionBuilder()
            .SetFeePayer(player2)
            .AddInstruction(GameProgramInstructions.JoinGame(accounts, ProgramId));

        return await SignAndSendAsync(builder, new List<Account> { player2 }, "join_game");
    }

    public async Task<bool> SettleGameTransaction(string gameAddress, string winnerAddress)
    {
        if (!TryGetActiveAccount(out var authority))
            return false;

        if (!TryParsePublicKey(gameAddress, "gameAddress", out var gamePda))
            return false;

        if (!TryParsePublicKey(winnerAddress, "winnerAddress", out var winner))
            return false;

        if (!TryDeriveVaultPda(gamePda, out var vaultPda))
        {
            Debug.LogError("Failed to derive vault PDA.");
            return false;
        }

        var accounts = new SettleGameAccounts
        {
            Game = gamePda,
            Vault = vaultPda,
            Winner = winner,
            Authority = authority.PublicKey,
            SystemProgram = SystemProgram.ProgramIdKey
        };

        var builder = new TransactionBuilder()
            .SetFeePayer(authority)
            .AddInstruction(GameProgramInstructions.SettleGame(accounts, winner, ProgramId));

        return await SignAndSendAsync(builder, new List<Account> { authority }, "settle_game");
    }

    public async Task<bool> RefundGameTransaction(
        string gameAddress,
        string player1Address,
        string player2Address,
        bool player2MustSign,
        Account player2Signer = null)
    {
        if (!TryGetActiveAccount(out var signer))
            return false;

        if (!TryParsePublicKey(gameAddress, "gameAddress", out var gamePda))
            return false;

        if (!TryParsePublicKey(player1Address, "player1Address", out var player1))
            return false;

        if (!TryParsePublicKey(player2Address, "player2Address", out var player2))
            return false;

        if (signer.PublicKey.Key != player1.Key)
        {
            Debug.LogError("Active wallet must match player1 for refund.");
            return false;
        }

        if (!TryDeriveVaultPda(gamePda, out var vaultPda))
        {
            Debug.LogError("Failed to derive vault PDA.");
            return false;
        }

        var accounts = new RefundAccounts
        {
            Game = gamePda,
            Vault = vaultPda,
            Player1 = player1,
            Player2 = player2,
            SystemProgram = SystemProgram.ProgramIdKey
        };

        var builder = new TransactionBuilder()
            .SetFeePayer(signer)
            .AddInstruction(GameProgramInstructions.Refund(accounts, player2MustSign, ProgramId));

        var signers = new List<Account> { signer };
        if (player2MustSign)
        {
            if (signer.PublicKey.Key == player2.Key)
            {
                return await SignAndSendAsync(builder, signers, "refund");
            }

            if (player2Signer == null)
            {
                Debug.LogError("Joined-state refund requires an additional signer for player2.");
                return false;
            }

            if (player2Signer.PublicKey.Key != player2.Key)
            {
                Debug.LogError("player2Signer does not match player2Address.");
                return false;
            }

            signers.Add(player2Signer);
        }

        return await SignAndSendAsync(builder, signers, "refund");
    }

    public bool TryDeriveGameAndVaultAddresses(
        string player1Address,
        string authorityAddress,
        out string gameAddress,
        out string vaultAddress)
    {
        gameAddress = string.Empty;
        vaultAddress = string.Empty;

        if (!TryParsePublicKey(player1Address, "player1Address", out var player1))
            return false;
        if (!TryParsePublicKey(authorityAddress, "authorityAddress", out var authority))
            return false;
        if (!TryDeriveGamePda(player1, authority, out var gamePda))
            return false;
        if (!TryDeriveVaultPda(gamePda, out var vaultPda))
            return false;

        gameAddress = gamePda.Key;
        vaultAddress = vaultPda.Key;
        return true;
    }

    private async Task<bool> SignAndSendAsync(TransactionBuilder builder, List<Account> signers, string instructionName)
    {
        try
        {
            var blockHashResult = await Web3.Rpc.GetLatestBlockHashAsync();
            if (!blockHashResult.WasSuccessful || blockHashResult.Result == null)
            {
                Debug.LogError("Failed to get latest blockhash: " + blockHashResult.Reason);
                return false;
            }

            builder.SetRecentBlockHash(blockHashResult.Result.Value.Blockhash);

            var tx = Transaction.Deserialize(builder.Build(signers));
            var sendResult = await Web3.Wallet.SignAndSendTransaction(tx);
            if (sendResult?.Result == null)
            {
                Debug.LogError($"Transaction failed for {instructionName}.");
                return false;
            }

            LastSignature = sendResult.Result;
            await Web3.Rpc.ConfirmTransaction(sendResult.Result, Commitment.Confirmed);

            var cluster = Web3.Wallet.RpcCluster.ToString().ToLowerInvariant();
            Debug.Log(
                $"{instructionName} confirmed: https://explorer.solana.com/tx/{sendResult.Result}?cluster={cluster}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending {instructionName} transaction: {e.Message}");
            return false;
        }
    }

    private static bool TryGetActiveAccount(out Account account)
    {
        account = Web3.Account;
        if (Web3.Instance == null || Web3.Wallet == null || Web3.Rpc == null || account == null)
        {
            Debug.LogError("Web3 wallet is not ready.");
            return false;
        }
        return true;
    }

    private static bool TryParsePublicKey(string value, string label, out PublicKey key)
    {
        try
        {
            key = new PublicKey(value);
            return true;
        }
        catch (Exception e)
        {
            key = default(PublicKey);
            Debug.LogError($"Invalid {label}: {e.Message}");
            return false;
        }
    }

    private static bool TryDeriveGamePda(PublicKey player1, PublicKey authority, out PublicKey gamePda)
    {
        return PublicKey.TryFindProgramAddress(
            new[] { GameSeedBytes, player1.KeyBytes, authority.KeyBytes },
            ProgramId,
            out gamePda,
            out _);
    }

    private static bool TryDeriveVaultPda(PublicKey gamePda, out PublicKey vaultPda)
    {
        return PublicKey.TryFindProgramAddress(
            new[] { VaultSeedBytes, gamePda.KeyBytes },
            ProgramId,
            out vaultPda,
            out _);
    }
}
