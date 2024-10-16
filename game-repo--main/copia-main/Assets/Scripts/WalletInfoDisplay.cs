using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Solana.Unity.SDK;
using Solana.Unity.Rpc.Types;
using Solana.Unity.Wallet;
using TMPro;

public class WalletInfoDisplay : MonoBehaviour
{
   
    public TMP_Text walletAddressText;
    //public Text walletBalanceText;

    private async void Start()
    {
        // Get the wallet account from Web3 instance
        var creatorAccount = Web3.Account;
        
        if (creatorAccount != null)
        {
            // Display wallet address
            walletAddressText.text = "Wallet Address: " + creatorAccount.PublicKey.ToString();

            // Fetch and display wallet balance
            //var balance = await Web3.Instance.WalletBase.GetBalance(Commitment.Confirmed);
            //walletBalanceText.text = "Wallet Balance: " + balance.ToString() + " SOL";
        }
        else
        {
            walletAddressText.text = "Wallet not connected";
            //walletBalanceText.text = "";
        }
    }
}
