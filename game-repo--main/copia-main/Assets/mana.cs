#if !UNITY_SERVER
using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Authentication;
using UnityEngine.UI;

/// <summary>
/// Solana bet creation + server connection flow.
/// Creates a Solana game transaction, then connects to a dedicated server.
/// Client-only (excluded from server builds).
/// </summary>
public class mana : MonoBehaviour
{
    public Button createBetButton;
    public Button acceptBetButton;
    public Button loginButton;

    public ttservise gameService;

    async void Start()
    {
        try
        {
            var options = new InitializationOptions()
                .SetEnvironmentName("production");
            await UnityServices.InitializeAsync(options);
            Debug.Log("Unity Services successfully initialized.");

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log("Anonymous sign-in successful.");
            }
        }
        catch
        {
            Debug.LogError("Failed to initialize Unity Services.");
        }

        // Solana UI — only activate if buttons are assigned in Inspector
        if (createBetButton != null) createBetButton.gameObject.SetActive(false);
        if (acceptBetButton != null) acceptBetButton.gameObject.SetActive(false);
        if (loginButton != null) loginButton.gameObject.SetActive(true);
    }

    public void Logi1n()
    {
        if (loginButton != null) loginButton.gameObject.SetActive(false);
        if (createBetButton != null) createBetButton.gameObject.SetActive(true);
        if (acceptBetButton != null) acceptBetButton.gameObject.SetActive(true);
    }

    /// <summary>
    /// Create Solana bet + allocate/connect to dedicated server.
    /// </summary>
    public async void createBe3t()
    {
        if (createBetButton != null) createBetButton.gameObject.SetActive(false);
        if (acceptBetButton != null) acceptBetButton.gameObject.SetActive(false);

        // Solana game transaction (client-side blockchain)
        bool success = await gameService.CreateGameTransaction(20000000UL);

        if (success)
        {
            // Connect to dedicated server instead of StartHost
            if (Kart.Multiplayer.Instance != null)
            {
                await Kart.Multiplayer.Instance.CreateLobby();
            }
            else
            {
                Debug.LogError("Multiplayer instance not found.");
                if (createBetButton != null) createBetButton.gameObject.SetActive(true);
            }
        }
        else
        {
            Debug.LogError("Game creation failed.");
            if (createBetButton != null) createBetButton.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Accept a bet + join the dedicated server.
    /// </summary>
    public async void acceptBe3t()
    {
        if (createBetButton != null) createBetButton.gameObject.SetActive(false);
        if (acceptBetButton != null) acceptBetButton.gameObject.SetActive(false);

        // Join via lobby (which reads server IP/port)
        if (Kart.Multiplayer.Instance != null)
        {
            await Kart.Multiplayer.Instance.QuickJoinLobby();
        }
        else
        {
            Debug.LogError("Multiplayer instance not found.");
        }
    }
}
#endif
