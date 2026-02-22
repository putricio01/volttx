using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Auto-matchmaking manager. Handles finding random opponents.
/// Abstracts the matchmaking provider — works with Unity Matchmaker or custom backend.
/// </summary>
public class MatchmakingManager : MonoBehaviour
{
    public static MatchmakingManager Instance { get; private set; }

    [Header("Matchmaking Settings")]
    [SerializeField] float matchmakingTimeout = 60f; // seconds

    public enum MatchmakingState
    {
        Idle,
        Searching,
        Found,
        Failed
    }

    public MatchmakingState State { get; private set; } = MatchmakingState.Idle;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Start searching for a match. Returns server connection info when a match is found.
    /// </summary>
    public async Task<(string ip, ushort port)?> FindMatch()
    {
        if (State == MatchmakingState.Searching)
        {
            Debug.LogWarning("[Matchmaking] Already searching for a match.");
            return null;
        }

        State = MatchmakingState.Searching;
        Debug.Log("[Matchmaking] Searching for opponent...");

        try
        {
            // ============================================================
            // OPTION A: Unity Matchmaker (com.unity.services.multiplayer)
            // Uncomment when you configure Unity Matchmaker + Multiplay:
            //
            // var ticket = await MatchmakerService.Instance.CreateTicketAsync(
            //     new List<Player> { new Player(Kart.Multiplayer.Instance.PlayerId) },
            //     new CreateTicketOptions { QueueName = "default-queue" }
            // );
            //
            // // Poll for match result
            // while (true)
            // {
            //     await Task.Delay(2000);
            //     var status = await MatchmakerService.Instance.GetTicketAsync(ticket.Id);
            //     if (status.Type == typeof(MultiplayAssignment))
            //     {
            //         var assignment = status.Value as MultiplayAssignment;
            //         if (assignment.Status == MultiplayAssignment.StatusOptions.Found)
            //         {
            //             State = MatchmakingState.Found;
            //             return (assignment.Ip, (ushort)assignment.Port);
            //         }
            //     }
            // }
            // ============================================================

            // OPTION B: Custom backend (placeholder)
            // TODO: Replace with your REST API calls
            // POST /api/matchmaking/queue { playerId: "...", gameMode: "1v1" }
            // GET  /api/matchmaking/status/{ticketId}

            // For now: use Kart.Multiplayer to allocate a server directly (testing)
            var serverInfo = await Kart.Multiplayer.Instance.AllocateServer();
            State = MatchmakingState.Found;

            Debug.Log($"[Matchmaking] Match found! Server: {serverInfo.ip}:{serverInfo.port}");
            return serverInfo;
        }
        catch (System.Exception e)
        {
            State = MatchmakingState.Failed;
            Debug.LogError($"[Matchmaking] Failed: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Cancel an active matchmaking search.
    /// </summary>
    public void CancelSearch()
    {
        if (State != MatchmakingState.Searching) return;

        State = MatchmakingState.Idle;
        Debug.Log("[Matchmaking] Search cancelled.");

        // TODO: Cancel the matchmaking ticket if using Unity Matchmaker
        // await MatchmakerService.Instance.DeleteTicketAsync(ticketId);
    }

    /// <summary>
    /// Find a match and connect automatically.
    /// </summary>
    public async Task FindMatchAndConnect()
    {
        var result = await FindMatch();
        if (result.HasValue)
        {
            Kart.Multiplayer.Instance.ConnectToServer(result.Value.ip, result.Value.port);
        }
    }
}
