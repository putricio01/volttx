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


namespace Kart {

    public class mana2 : MonoBehaviour
    {
        // Reference to the buttons
        public Button createBetButton;
        public Button acceptBetButton;
        public Button loginButton;

        
        // Reference to the ttservise script
        public ttservise gameService;

        private bool isHostStarted = false;

        void Awake(){
            createBetButton.onClick.AddListener(createBet);
            acceptBetButton.onClick.AddListener(acceptBet);
            loginButton.onClick.AddListener(Login);
        } 

        async void Start()
        {
            // Only show the login button at first, hide the others
            DontDestroyOnLoad(this);
            
            createBetButton.gameObject.SetActive(true);
            acceptBetButton.gameObject.SetActive(true);
            loginButton.gameObject.SetActive(false); // Ensure login button is visible
        }

        public void Login()
        {
            // Hide the login button
            loginButton.gameObject.SetActive(false);

        
                // If the host has not started, show the create bet button
                createBetButton.gameObject.SetActive(true);
                acceptBetButton.gameObject.SetActive(true);
            
        }
    

        
        public async void createBet()
        {
            createBetButton.gameObject.SetActive(false);
            acceptBetButton.gameObject.SetActive(false);

            // Call CreateGameTransaction from ttservise to trigger the Solana game creation
            //bool success = await gameService.CreateGameTransaction(20000000UL);  //200000000UL Example entry amount (1 SOL in lamports)

            
            //if (success)
            //{
                await Multiplayer.Instance.CreateLobby();
            //}
                // Try to allocate a relay server and start the host
                
        }


        
        public async void acceptBet()
        {
            Debug.Log("acceptBet button clicked");
            createBetButton.gameObject.SetActive(false);
            acceptBetButton.gameObject.SetActive(false);


            await Multiplayer.Instance.QuickJoinLobby();

            // Get the join code from player 1 (you can retrieve this from your UI or another way)
        
        }
    }
}