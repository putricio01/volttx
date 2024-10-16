
using UnityEngine;
using TMPro;
using Unity.Netcode;

public class GameTimer : NetworkBehaviour
{
    public gol lol;
    public gol2 lol2;
    public TMP_Text timerText;
    //private float timeRemaining; // No need to set here, as we will set it when the second player joins
    private NetworkVariable<float> timeRemaining = new NetworkVariable<float>(180f);

    private bool timerIsRunning = false;
    public Ball ball; // Assign the ball GameObject in the inspector
    public PlayerRespawner playerRespawner;

    private void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        // Initialize timer for 3 minutes when the second player joins
      
     
        
    }
   

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer){
        // Check if this is the second player joining
        if (NetworkManager.Singleton.ConnectedClients.Count == 2)
        {
            timerIsRunning = true; // Start the timer
            playerRespawner.RespawnPlayersAfterGoal();
            lol.ResetScoreServerRpc();
            lol2.ResetScoreServerRpc();

        }
        }
    }

    void Update()
    {
        if (timerIsRunning && timeRemaining.Value > 0)
        {
            timeRemaining.Value -= Time.deltaTime;
            UpdateTimerDisplay();
        }
        else if (timerIsRunning)
        {
            EndGame();
            timerIsRunning = false; // Stop the timer
        }
    }

    void UpdateTimerDisplay()
    {
        int minutes = Mathf.FloorToInt(timeRemaining.Value / 60);
        int seconds = Mathf.FloorToInt(timeRemaining.Value % 60);
        timeClientRpc(minutes,seconds);
       // timerText.text = $"{minutes:00}:{seconds:00}";
    }
    [ClientRpc]
    private void timeClientRpc(int min,int sec){
        timerText.text = $"{min:00}:{sec:00}";
    }

    void EndGame()
{
    // Log out players and remove the ball
    NetworkManager.Singleton.Shutdown();

    if (ball != null)
    {
        Destroy(ball); // This will remove the ball from the scene
    }

    // Optionally, you can also load a different scene or show a game over screen here.


        // Optionally, you can also load a different scene or show a game over screen here.
    }
}
