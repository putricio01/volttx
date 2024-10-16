using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Solana.Unity.Rpc.Builders;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Wallet;
using Solana.Unity.SDK;
using System.Text;
using TtGame;
using TtGame.Program;
using Solana.Unity.Rpc.Types;
using Solana.Unity.Programs;
using UnityEngine;

public class ttservise : MonoBehaviour 
{
    // Set your Program ID
    public static PublicKey ProgramId = new PublicKey("GvgvNoQLrtqQsp1XYJ2hNQ3tgXCreeHMWSpLaSTQ9cj9");

    // Ensure Web3 instance exists
    private Web3 web3;

    public void Awake()
    {
        if (Web3.Instance == null)
        {
            Debug.LogError("Web3 instance is not initialized.");
            return;
        }
        web3 = Web3.Instance;
    }

    public void Start()
    {
        // Initialize or do any setup if needed
    }

    public async Task<bool> CreateGameTransaction(ulong entryAmount)
    {
        try {
            // Define accounts
            var gameAccount = new Account();  // New Game account
            var creatorAccount = Web3.Account;  // Creator of the game (Web3.Account is assumed to be initialized)

            // Prepare the transaction builder
            var blockHashResult = await Web3.Rpc.GetLatestBlockHashAsync();
            if (blockHashResult.WasSuccessful && blockHashResult.Result != null)
            {
                var blockHash = blockHashResult.Result.Value.Blockhash;
                var transactionBuilder = new TransactionBuilder()
                    .SetRecentBlockHash(blockHash)
                    .SetFeePayer(creatorAccount);  // Fee payer is the creator

                // Define the CreateGame accounts
                var createGameAccounts = new TtGame.Program.CreateGameAccounts
                {
                    Game = gameAccount.PublicKey,
                    Creator = creatorAccount.PublicKey,
                    SystemProgram = SystemProgram.ProgramIdKey
                };

                // Add the createGame instruction to the transaction with dynamic entryAmount
                transactionBuilder.AddInstruction(TtGame.Program.TtGameProgram.CreateGame(
                    createGameAccounts,
                    entryAmount,  // Dynamically use the passed entryAmount
                    ProgramId
                ));

                // Build and sign the transaction
                var tx = Transaction.Deserialize(transactionBuilder.Build(new List<Account> { creatorAccount, gameAccount }));

                // Sign and send the transaction
                var res = await Web3.Wallet.SignAndSendTransaction(tx);

                // Show Confirmation
                if (res?.Result != null)
                {
                    await Web3.Rpc.ConfirmTransaction(res.Result, Commitment.Confirmed);
                    Debug.Log("Game created, see transaction at https://explorer.solana.com/tx/" 
                              + res.Result + "?cluster=" + Web3.Wallet.RpcCluster.ToString().ToLower());

                    return true;
                }
                else
                {
                    Debug.LogError("Transaction failed.");
                    return false;  // Return failure
                }
            }
            else
            {
                Debug.LogError("Failed to get latest block hash: " + blockHashResult.Reason);
                return false;  // Return failure
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error during CreateGameTransaction: " + e.Message);
            return false;  // Return failure
        }
    }
}