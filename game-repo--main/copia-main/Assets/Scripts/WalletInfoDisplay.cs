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

    private void Start()
    {
        // Solana wallet display disabled for now — enable when wallet integration is ready
        if (walletAddressText != null)
            walletAddressText.text = "Wallet not connected";

        // Disable this component since Solana isn't active yet
        enabled = false;
    }
}
