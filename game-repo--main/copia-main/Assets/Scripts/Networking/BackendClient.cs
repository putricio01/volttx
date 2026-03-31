#if !UNITY_SERVER
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// HTTP client for the VoltTx Rust backend.
/// Used by clients (not server) to manage challenges.
/// </summary>
public class BackendClient
{
    readonly string _baseUrl;

    public BackendClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    // ── Data models ─────────────────────────────────────

    [Serializable]
    public class ChallengeInfo
    {
        public string game_pda;
        public string creator_pubkey;
        public ulong entry_amount;
        public ulong match_id;
        public long created_at;
        public string status;

        public double WagerSol => entry_amount / 1_000_000_000.0;
    }

    [Serializable]
    public class ServerAssignment
    {
        public string server_ip;
        public ushort server_port;
        public string status;
    }

    [Serializable]
    public class ChallengeStatus
    {
        public string status;
        public string server_ip;
        public ushort server_port;
    }

    // JSON wrapper for arrays (Unity's JsonUtility needs a root object)
    [Serializable]
    class ChallengeListWrapper
    {
        public List<ChallengeInfo> challenges;
    }

    [Serializable]
    public class RegisterChallengeResponse
    {
        public bool ok;
        public string server_ip;
        public ushort server_port;
    }

    // ── Request bodies ──────────────────────────────────

    [Serializable]
    class RegisterChallengeBody
    {
        public string game_pda;
        public string creator_pubkey;
        public ulong entry_amount;
        public ulong match_id;
    }

    [Serializable]
    class AcceptChallengeBody
    {
        public string acceptor_pubkey;
    }

    // ── Public API ──────────────────────────────────────

    /// <summary>
    /// Register a new challenge with the backend after on-chain create_game succeeds.
    /// Returns server assignment so the creator can connect immediately.
    /// </summary>
    public async Task<ServerAssignment> RegisterChallenge(string gamePda, string creatorPubkey, ulong entryAmount, ulong matchId)
    {
#if UNITY_EDITOR
        return LocalChallengeStore.RegisterChallenge(gamePda, creatorPubkey, entryAmount, matchId);
#else
        var body = new RegisterChallengeBody
        {
            game_pda = gamePda,
            creator_pubkey = creatorPubkey,
            entry_amount = entryAmount,
            match_id = matchId
        };

        var result = await PostJsonAsync($"{_baseUrl}/v1/challenges", JsonUtility.ToJson(body));
        if (!result.success)
            return null;

        var resp = JsonUtility.FromJson<RegisterChallengeResponse>(result.body);
        if (resp == null || !resp.ok)
            return null;

        return new ServerAssignment
        {
            server_ip = resp.server_ip,
            server_port = resp.server_port,
            status = "created"
        };
#endif
    }

    /// <summary>
    /// Fetch the list of open challenges from the backend.
    /// </summary>
    public async Task<List<ChallengeInfo>> GetOpenChallenges()
    {
#if UNITY_EDITOR
        return LocalChallengeStore.GetOpenChallenges();
#else
        var result = await GetAsync($"{_baseUrl}/v1/challenges");
        if (!result.success)
            return new List<ChallengeInfo>();

        var wrapper = JsonUtility.FromJson<ChallengeListWrapper>(result.body);
        return wrapper?.challenges ?? new List<ChallengeInfo>();
#endif
    }

    /// <summary>
    /// Accept a challenge. Backend assigns a server from the pool.
    /// Returns server IP/port, or null on failure.
    /// </summary>
    public async Task<ServerAssignment> AcceptChallenge(string gamePda, string acceptorPubkey)
    {
#if UNITY_EDITOR
        return LocalChallengeStore.AcceptChallenge(gamePda, acceptorPubkey);
#else
        var body = new AcceptChallengeBody { acceptor_pubkey = acceptorPubkey };
        var result = await PostJsonAsync(
            $"{_baseUrl}/v1/challenges/{Uri.EscapeDataString(gamePda)}/accept",
            JsonUtility.ToJson(body));

        if (!result.success)
            return null;

        return JsonUtility.FromJson<ServerAssignment>(result.body);
#endif
    }

    /// <summary>
    /// Poll challenge status (for the creator waiting for an opponent).
    /// Returns server info once the challenge is accepted and a server is assigned.
    /// </summary>
    public async Task<ChallengeStatus> GetChallengeStatus(string gamePda)
    {
#if UNITY_EDITOR
        return LocalChallengeStore.GetChallengeStatus(gamePda);
#else
        var result = await GetAsync($"{_baseUrl}/v1/challenges/{Uri.EscapeDataString(gamePda)}/status");
        if (!result.success)
            return null;

        return JsonUtility.FromJson<ChallengeStatus>(result.body);
#endif
    }

    // ── HTTP helpers ────────────────────────────────────

    struct HttpResult
    {
        public bool success;
        public string body;
    }

    async Task<HttpResult> GetAsync(string url)
    {
        using var req = UnityWebRequest.Get(url);
        req.SetRequestHeader("Content-Type", "application/json");

        var op = req.SendWebRequest();
        while (!op.isDone)
            await Task.Yield();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[BackendClient] GET {url} failed: {req.error}");
            return new HttpResult { success = false };
        }

        return new HttpResult { success = true, body = req.downloadHandler.text };
    }

    async Task<HttpResult> PostJsonAsync(string url, string jsonBody)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(bodyBytes);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        var op = req.SendWebRequest();
        while (!op.isDone)
            await Task.Yield();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[BackendClient] POST {url} failed: {req.error} — {req.downloadHandler?.text}");
            return new HttpResult { success = false };
        }

        return new HttpResult { success = true, body = req.downloadHandler.text };
    }
}
#endif
