using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Authentication;
using Unity.Networking.Transport.Relay;
using UnityEngine.UI;

public class mana : MonoBehaviour
{
    // Reference to the buttons
    public Button createBetButton;
    public Button acceptBetButton;
    public Button loginButton;

    private string currentJoinCode = string.Empty; // Variable to store the join code


    // Reference to the ttservise script
    public ttservise gameService;

    private bool isHostStarted = false;

    async void Start()
    {
        // Only show the login button at first, hide the others
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
        
        
        createBetButton.gameObject.SetActive(false);
        acceptBetButton.gameObject.SetActive(false);
        loginButton.gameObject.SetActive(true); // Ensure login button is visible
    }

    public void Logi1n()
    {
        // Hide the login button
        loginButton.gameObject.SetActive(false);

        // Check if the host is already started
        if (isHostStarted)
        {
            // If the host has started, only show the accept bet button
            acceptBetButton.gameObject.SetActive(true);
        }
        else
        {
            // If the host has not started, show the create bet button
            createBetButton.gameObject.SetActive(true);
            acceptBetButton.gameObject.SetActive(true);
        }
    }
   

    
    public async void createBe3t()
    {
        createBetButton.gameObject.SetActive(false);
        acceptBetButton.gameObject.SetActive(false);

        // Call CreateGameTransaction from ttservise to trigger the Solana game creation
        bool success = await gameService.CreateGameTransaction(20000000UL);  //200000000UL Example entry amount (1 SOL in lamports)

        if (success)
        {
            // Try to allocate a relay server and start the host
            try
            {
                // Use Unity Relay to allocate the server and get a join code
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(1); // 1 client
                 currentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                Debug.Log("Relay join code: " + currentJoinCode);

                // Set up Relay server data
                RelayServerData relayServerData = new RelayServerData(allocation, "wss"); // Using DTLS for security "dtls"
                NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

                // Start the host
                NetworkManager.Singleton.StartHost();
                isHostStarted = true;
            }
            catch
            {
                Debug.LogError("Error during relay allocation.");
                createBetButton.gameObject.SetActive(true);  // Re-enable the createBet button if allocation fails
            }
        }
        else
        {
            Debug.LogError("Game creation failed, host will not start.");
            createBetButton.gameObject.SetActive(true);  // Re-enable the createBet button if game creation fails
        }
    }


    
    public async void acceptBe3t()
    {
        createBetButton.gameObject.SetActive(false);
        acceptBetButton.gameObject.SetActive(false);

        // Get the join code from player 1 (you can retrieve this from your UI or another way)
        string joinCode = currentJoinCode;  // This should be input dynamically

        // Use the join code to join the relay server
        JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

        // Set up Relay client data
        RelayServerData relayServerData = new RelayServerData(joinAllocation, "wss");
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

        // Start the client
        NetworkManager.Singleton.StartClient();
    }
}
