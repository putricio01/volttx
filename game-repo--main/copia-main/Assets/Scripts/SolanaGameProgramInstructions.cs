using System;
using System.Collections.Generic;
using Solana.Unity.Programs.Utilities;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Wallet;

namespace SolanaGame.Program
{
    public class CreateGameAccounts
    {
        public PublicKey Player1 { get; set; }
        public PublicKey Authority { get; set; }
        public PublicKey Game { get; set; }
        public PublicKey Vault { get; set; }
        public PublicKey SystemProgram { get; set; }
    }

    public class JoinGameAccounts
    {
        public PublicKey Player2 { get; set; }
        public PublicKey Game { get; set; }
        public PublicKey Vault { get; set; }
        public PublicKey SystemProgram { get; set; }
    }

    public class SettleGameAccounts
    {
        public PublicKey Game { get; set; }
        public PublicKey Vault { get; set; }
        public PublicKey Winner { get; set; }
        public PublicKey Authority { get; set; }
        public PublicKey SystemProgram { get; set; }
    }

    public class RefundAccounts
    {
        public PublicKey Game { get; set; }
        public PublicKey Vault { get; set; }
        public PublicKey Player1 { get; set; }
        public PublicKey Player2 { get; set; }
        public PublicKey SystemProgram { get; set; }
    }

    public static class GameProgramInstructions
    {
        public const string GameSeed = "game";
        public const string VaultSeed = "vault";

        private const ulong CreateGameDiscriminator = 14864373254080644476UL;
        private const ulong JoinGameDiscriminator = 9240450992125931627UL;
        private const ulong SettleGameDiscriminator = 2114095808068990560UL;
        private const ulong RefundDiscriminator = 3327826147897991170UL;

        public static TransactionInstruction CreateGame(CreateGameAccounts accounts, ulong entryAmount, PublicKey programId)
        {
            if (accounts == null) throw new ArgumentNullException(nameof(accounts));

            List<AccountMeta> keys = new List<AccountMeta>
            {
                AccountMeta.Writable(accounts.Player1, true),
                AccountMeta.ReadOnly(accounts.Authority, false),
                AccountMeta.Writable(accounts.Game, false),
                AccountMeta.Writable(accounts.Vault, false),
                AccountMeta.ReadOnly(accounts.SystemProgram, false)
            };

            byte[] data = new byte[16];
            int offset = 0;
            data.WriteU64(CreateGameDiscriminator, offset);
            offset += 8;
            data.WriteU64(entryAmount, offset);

            return new TransactionInstruction
            {
                Keys = keys,
                ProgramId = programId.KeyBytes,
                Data = data
            };
        }

        public static TransactionInstruction JoinGame(JoinGameAccounts accounts, PublicKey programId)
        {
            if (accounts == null) throw new ArgumentNullException(nameof(accounts));

            List<AccountMeta> keys = new List<AccountMeta>
            {
                AccountMeta.Writable(accounts.Player2, true),
                AccountMeta.Writable(accounts.Game, false),
                AccountMeta.Writable(accounts.Vault, false),
                AccountMeta.ReadOnly(accounts.SystemProgram, false)
            };

            byte[] data = new byte[8];
            data.WriteU64(JoinGameDiscriminator, 0);

            return new TransactionInstruction
            {
                Keys = keys,
                ProgramId = programId.KeyBytes,
                Data = data
            };
        }

        public static TransactionInstruction SettleGame(SettleGameAccounts accounts, PublicKey winner, PublicKey programId)
        {
            if (accounts == null) throw new ArgumentNullException(nameof(accounts));

            List<AccountMeta> keys = new List<AccountMeta>
            {
                AccountMeta.Writable(accounts.Game, false),
                AccountMeta.Writable(accounts.Vault, false),
                AccountMeta.Writable(accounts.Winner, false),
                AccountMeta.ReadOnly(accounts.Authority, true),
                AccountMeta.ReadOnly(accounts.SystemProgram, false)
            };

            byte[] data = new byte[40];
            int offset = 0;
            data.WriteU64(SettleGameDiscriminator, offset);
            offset += 8;
            data.WritePubKey(winner, offset);

            return new TransactionInstruction
            {
                Keys = keys,
                ProgramId = programId.KeyBytes,
                Data = data
            };
        }

        public static TransactionInstruction Refund(RefundAccounts accounts, bool player2IsSigner, PublicKey programId)
        {
            if (accounts == null) throw new ArgumentNullException(nameof(accounts));

            List<AccountMeta> keys = new List<AccountMeta>
            {
                AccountMeta.Writable(accounts.Game, false),
                AccountMeta.Writable(accounts.Vault, false),
                AccountMeta.Writable(accounts.Player1, true),
                AccountMeta.Writable(accounts.Player2, player2IsSigner),
                AccountMeta.ReadOnly(accounts.SystemProgram, false)
            };

            byte[] data = new byte[8];
            data.WriteU64(RefundDiscriminator, 0);

            return new TransactionInstruction
            {
                Keys = keys,
                ProgramId = programId.KeyBytes,
                Data = data
            };
        }
    }
}
