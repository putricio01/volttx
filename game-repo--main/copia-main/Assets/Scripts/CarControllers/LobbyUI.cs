using UnityEngine;
using UnityEngine.UI;

namespace Kart {
    /// <summary>
    /// UI for lobby management. Supports:
    /// - Create Lobby (for friends)
    /// - Join Lobby (for friends)
    /// - Find Match (auto-matchmaking for randoms)
    /// </summary>
    public class LobbyUI : MonoBehaviour {
        [SerializeField] Button createLobbyButton;
        [SerializeField] Button joinLobbyButton;
        [SerializeField] Button findMatchButton; // New: auto-matchmaking

        void Awake() {
            // Null-check buttons — they may be null on server (canvases disabled)
            if (createLobbyButton != null)
                createLobbyButton.onClick.AddListener(CreateGame);
            if (joinLobbyButton != null)
                joinLobbyButton.onClick.AddListener(JoinGame);
            if (findMatchButton != null)
                findMatchButton.onClick.AddListener(FindMatch);
        }

        async void CreateGame() {
            DisableAllButtons();
            await Multiplayer.Instance.CreateLobby();
        }

        async void JoinGame() {
            DisableAllButtons();
            await Multiplayer.Instance.QuickJoinLobby();
        }

        async void FindMatch() {
            DisableAllButtons();
            if (MatchmakingManager.Instance != null)
            {
                await MatchmakingManager.Instance.FindMatchAndConnect();
            }
            else
            {
                Debug.LogError("MatchmakingManager not found in scene!");
                EnableAllButtons();
            }
        }

        void DisableAllButtons() {
            createLobbyButton.gameObject.SetActive(false);
            joinLobbyButton.gameObject.SetActive(false);
            if (findMatchButton != null)
                findMatchButton.gameObject.SetActive(false);
        }

        void EnableAllButtons() {
            createLobbyButton.gameObject.SetActive(true);
            joinLobbyButton.gameObject.SetActive(true);
            if (findMatchButton != null)
                findMatchButton.gameObject.SetActive(true);
        }
    }
}
