

using UnityEngine;
using UnityEngine.UI;


namespace Kart {
    public class LobbyUI : MonoBehaviour {
        [SerializeField] Button createLobbyButton;
        [SerializeField] Button joinLobbyButton;
    

        void Awake() {
            createLobbyButton.onClick.AddListener(CreateGame);
            joinLobbyButton.onClick.AddListener(JoinGame);
        }

        async void CreateGame() {
            createLobbyButton.gameObject.SetActive(false);
            joinLobbyButton.gameObject.SetActive(false);
            await Multiplayer.Instance.CreateLobby();
            //loader.LoadNetwork(gameScene);
        }

        async void JoinGame() {
            createLobbyButton.gameObject.SetActive(false);
            joinLobbyButton.gameObject.SetActive(false);
            await Multiplayer.Instance.QuickJoinLobby();
            
        }
    }
}