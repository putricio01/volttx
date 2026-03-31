#if UNITY_SERVER
using System;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Server-only: registers this server instance in the backend's server pool
/// and sends periodic heartbeats so the backend knows the server is alive.
///
/// Env vars:
///   BACKEND_URL          — e.g. "http://backend:8000"
///   INTERNAL_HMAC_SECRET — shared secret with backend
///   SERVER_ID            — unique ID for this server instance (auto-generated if missing)
///   SERVER_IP            — this server's public IP (default: 127.0.0.1)
///   PORT                 — listening port (default: 7777)
/// </summary>
public class ServerPoolHeartbeat : MonoBehaviour
{
    public static ServerPoolHeartbeat Instance { get; private set; }

    string _serverId;
    string _serverIp;
    ushort _serverPort;
    string _backendUrl;
    string _hmacSecret;
    string _status = "idle";

    float _heartbeatTimer;
    const float HEARTBEAT_INTERVAL = 15f;

    bool _registered;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _backendUrl = (Environment.GetEnvironmentVariable("BACKEND_URL") ?? "http://127.0.0.1:8000").TrimEnd('/');
        _hmacSecret = Environment.GetEnvironmentVariable("INTERNAL_HMAC_SECRET") ?? "";
        _serverId = Environment.GetEnvironmentVariable("SERVER_ID") ?? Guid.NewGuid().ToString("N");
        _serverIp = Environment.GetEnvironmentVariable("SERVER_IP") ?? "127.0.0.1";

        string portStr = Environment.GetEnvironmentVariable("PORT");
        _serverPort = (!string.IsNullOrEmpty(portStr) && ushort.TryParse(portStr, out var p)) ? p : (ushort)7777;
    }

    async void Start()
    {
        await RegisterAsync();
    }

    void Update()
    {
        if (!_registered) return;

        _heartbeatTimer -= Time.deltaTime;
        if (_heartbeatTimer <= 0f)
        {
            _heartbeatTimer = HEARTBEAT_INTERVAL;
            _ = SendHeartbeatAsync();
        }
    }

    /// <summary>
    /// Mark this server as busy (match in progress).
    /// </summary>
    public void MarkBusy()
    {
        _status = "busy";
        Debug.Log($"[ServerPool] Status → busy");
    }

    /// <summary>
    /// Mark this server as idle (ready for new match).
    /// </summary>
    public void MarkIdle()
    {
        _status = "idle";
        Debug.Log($"[ServerPool] Status → idle");
        // Send immediate heartbeat so backend knows we're available
        _ = SendHeartbeatAsync();
    }

    async Task RegisterAsync()
    {
        var body = $"{{\"server_id\":\"{_serverId}\",\"ip\":\"{_serverIp}\",\"port\":{_serverPort},\"status\":\"{_status}\"}}";
        string url = $"{_backendUrl}/v1/servers/register";

        bool ok = await PostWithHmacAsync(url, body);
        if (ok)
        {
            _registered = true;
            _heartbeatTimer = HEARTBEAT_INTERVAL;
            Debug.Log($"[ServerPool] Registered: id={_serverId} at {_serverIp}:{_serverPort}");
        }
        else
        {
            Debug.LogError($"[ServerPool] Registration failed. Will retry on next heartbeat.");
            // Retry registration via heartbeat timer
            _registered = true; // Allow heartbeat loop to run
            _heartbeatTimer = 5f; // Retry sooner
        }
    }

    async Task SendHeartbeatAsync()
    {
        var body = $"{{\"status\":\"{_status}\"}}";
        string url = $"{_backendUrl}/v1/servers/{_serverId}/heartbeat";

        bool ok = await PutWithHmacAsync(url, body);
        if (!ok)
        {
            Debug.LogWarning("[ServerPool] Heartbeat failed.");
        }
    }

    async Task<bool> PostWithHmacAsync(string url, string jsonBody)
    {
        return await SendWithHmacAsync(url, "POST", jsonBody);
    }

    async Task<bool> PutWithHmacAsync(string url, string jsonBody)
    {
        return await SendWithHmacAsync(url, "PUT", jsonBody);
    }

    async Task<bool> SendWithHmacAsync(string url, string method, string jsonBody)
    {
        try
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string nonce = Guid.NewGuid().ToString("N");
            string signature = ComputeHmac(timestamp, nonce, jsonBody);

            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            using var req = new UnityWebRequest(url, method);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-Timestamp", timestamp.ToString());
            req.SetRequestHeader("X-Nonce", nonce);
            req.SetRequestHeader("X-Signature", signature);

            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            return req.result == UnityWebRequest.Result.Success;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ServerPool] {method} {url} exception: {e.Message}");
            return false;
        }
    }

    string ComputeHmac(long timestamp, string nonce, string body)
    {
        string message = $"{timestamp}.{nonce}.{body}";
        byte[] keyBytes = Encoding.UTF8.GetBytes(_hmacSecret);
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);

        using var hmac = new HMACSHA256(keyBytes);
        byte[] hash = hmac.ComputeHash(messageBytes);

        var hex = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
            hex.AppendFormat("{0:x2}", b);

        return $"sha256={hex}";
    }
}
#endif
