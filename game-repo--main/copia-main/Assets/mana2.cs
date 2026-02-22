#if !UNITY_SERVER
using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

namespace Kart {
    /// <summary>
    /// Lobby-based bet creation/joining with Solana integration.
    /// Client-only (excluded from server builds).
    /// </summary>
    public class mana2 : MonoBehaviour
    {
        public Button createBetButton;
        public Button acceptBetButton;
        public Button loginButton;
        public ttservise gameService;

        private bool isHostStarted = false;

        void Awake()
        {
            if (createBetButton != null) createBetButton.onClick.AddListener(createBet);
            if (acceptBetButton != null) acceptBetButton.onClick.AddListener(acceptBet);
            if (loginButton != null) loginButton.onClick.AddListener(Login);
        }

        async void Start()
        {
            DontDestroyOnLoad(this);
            if (createBetButton != null) createBetButton.gameObject.SetActive(true);
            if (acceptBetButton != null) acceptBetButton.gameObject.SetActive(true);
            if (loginButton != null) loginButton.gameObject.SetActive(false);
        }

        public void Login()
        {
            if (loginButton != null) loginButton.gameObject.SetActive(false);
            if (createBetButton != null) createBetButton.gameObject.SetActive(true);
            if (acceptBetButton != null) acceptBetButton.gameObject.SetActive(true);
        }

        public async void createBet()
        {
            if (createBetButton != null) createBetButton.gameObject.SetActive(false);
            if (acceptBetButton != null) acceptBetButton.gameObject.SetActive(false);

            // TODO: Uncomment Solana transaction when ready:
            // bool success = await gameService.CreateGameTransaction(20000000UL);
            // if (success) { ... }

            if (Multiplayer.Instance != null)
                await Multiplayer.Instance.CreateLobby();
        }

        public async void acceptBet()
        {
            Debug.Log("acceptBet button clicked");
            if (createBetButton != null) createBetButton.gameObject.SetActive(false);
            if (acceptBetButton != null) acceptBetButton.gameObject.SetActive(false);

            if (Multiplayer.Instance != null)
                await Multiplayer.Instance.QuickJoinLobby();
        }
    }
}
#endif
