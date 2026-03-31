#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Server-only HTTP client for calling the backend's POST /v1/finalize endpoint.
/// Signs requests with HMAC-SHA256 matching the backend's internal_auth.rs expectations.
///
/// Env vars:
///   BACKEND_URL          — e.g. "http://backend:8000"
///   INTERNAL_HMAC_SECRET — shared secret with backend
/// </summary>
public static class ServerFinalizeClient
{
    static string ResolveBackendUrl()
    {
        string fromEnv = Environment.GetEnvironmentVariable("BACKEND_URL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        var cfg = GameConfig.Load();
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.backendUrl))
            return cfg.backendUrl.Trim();

        return "http://127.0.0.1:8000";
    }

    static string ResolveHmacSecret()
    {
        string fromEnv = Environment.GetEnvironmentVariable("INTERNAL_HMAC_SECRET");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        var cfg = GameConfig.Load();
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.internalHmacSecret))
            return cfg.internalHmacSecret;

        return string.Empty;
    }

    [Serializable]
    class FinalizeRequest
    {
        public string game_pda;
        public string outcome;
        public string winner_pubkey;
        public string reason_code;
        public string reason_detail;
        public string idempotency_key;
    }

    /// <summary>
    /// Send a finalization request to the backend.
    /// </summary>
    /// <param name="gamePda">On-chain game PDA (base58)</param>
    /// <param name="outcome">"winner" or "broken"</param>
    /// <param name="winnerPubkey">Winner's wallet pubkey (null if outcome=broken)</param>
    /// <param name="reasonCode">Short reason code (e.g. "goal_scored", "disconnect_forfeit")</param>
    /// <param name="reasonDetail">Optional detailed description</param>
    /// <param name="idempotencyKey">Unique key to prevent duplicate processing</param>
    public static async Task<bool> Finalize(
        string gamePda,
        string outcome,
        string winnerPubkey,
        string reasonCode,
        string reasonDetail,
        string idempotencyKey)
    {
        string backendUrl = ResolveBackendUrl().TrimEnd('/');
        string hmacSecret = ResolveHmacSecret();
        if (string.IsNullOrWhiteSpace(hmacSecret))
        {
            Debug.LogError("[ServerFinalize] INTERNAL_HMAC_SECRET missing. Cannot call /v1/finalize.");
            return false;
        }

        var body = new FinalizeRequest
        {
            game_pda = gamePda,
            outcome = outcome,
            winner_pubkey = winnerPubkey ?? "",
            reason_code = reasonCode,
            reason_detail = reasonDetail ?? "",
            idempotency_key = idempotencyKey
        };

        string jsonBody = JsonUtility.ToJson(body);
        string url = $"{backendUrl}/v1/finalize";

        // Generate HMAC headers matching backend internal_auth.rs
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string nonce = Guid.NewGuid().ToString("N");
        string signature = ComputeHmacSignature(hmacSecret, timestamp, nonce, jsonBody);

        Debug.Log($"[ServerFinalize] POST {url} — game_pda={ShortKey(gamePda)} outcome={outcome}");

        try
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-Timestamp", timestamp.ToString());
            req.SetRequestHeader("X-Nonce", nonce);
            req.SetRequestHeader("X-Signature", signature);

            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[ServerFinalize] POST failed: {req.responseCode} {req.error} — {req.downloadHandler?.text}");
                return false;
            }

            Debug.Log($"[ServerFinalize] Success: {req.downloadHandler.text}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ServerFinalize] Exception: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Compute HMAC-SHA256 signature: sha256=hex(hmac(secret, "{timestamp}.{nonce}.{body}"))
    /// Matches backend internal_auth.rs verify_internal_hmac().
    /// </summary>
    static string ComputeHmacSignature(string secret, long timestamp, string nonce, string body)
    {
        string message = $"{timestamp}.{nonce}.{body}";
        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);

        using var hmac = new HMACSHA256(keyBytes);
        byte[] hash = hmac.ComputeHash(messageBytes);

        var hex = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
            hex.AppendFormat("{0:x2}", b);

        return $"sha256={hex}";
    }

    static string ShortKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "(none)";
        if (key.Length <= 12) return key;
        return $"{key[..6]}…{key[^6..]}";
    }
}
#endif
