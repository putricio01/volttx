#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// File-based challenge store for MPPM editor testing.
///
/// MPPM virtual players run as SEPARATE PROCESSES, so static variables
/// are NOT shared. Instead we use a shared JSON file on disk that all
/// MPPM instances can read/write.
///
/// Replaces the Rust backend during editor testing.
/// </summary>
public static class LocalChallengeStore
{
    // Shared file path — must resolve to the REAL project root, not a VP clone.
    // In MPPM virtual players, Application.dataPath points to Library/VP/<tag>/Assets
    // instead of the actual project Assets folder. We detect this and walk up to
    // the true project root so ALL processes read/write the SAME file.
    static readonly string STORE_FILE = Path.Combine(
        GetProjectRoot(), "Temp", "mppm_challenges.json");

    static string GetProjectRoot()
    {
        string dataPath = Application.dataPath; // ends with "/Assets"
        string parent = Path.GetFullPath(Path.Combine(dataPath, ".."));

        // VP instances have dataPath like: .../Library/VP/<tag>/Assets
        // Detect this and walk up to the real project root.
        string libraryVP = Path.Combine("Library", "VP");
        if (parent.Contains(libraryVP))
        {
            // parent = .../Library/VP/<tag>
            // Go up 3 levels: <tag> -> VP -> Library -> ProjectRoot
            string projectRoot = Path.GetFullPath(Path.Combine(parent, "..", "..", ".."));
            Debug.Log($"[LocalChallengeStore] VP detected. Project root: {projectRoot}");
            return projectRoot;
        }

        return parent;
    }

    // Editor server address (the MPPM server instance).
    const string EDITOR_SERVER_IP = "127.0.0.1";
    const ushort EDITOR_SERVER_PORT = 7777;

    // ── Serializable wrapper for JSON persistence ──

    [Serializable]
    class StoreData
    {
        public List<ChallengeEntry> challenges = new();
    }

    [Serializable]
    class ChallengeEntry
    {
        public string game_pda;
        public string creator_pubkey;
        // Stored as long because Unity's JsonUtility doesn't serialize ulong.
        // Wager amounts in lamports fit comfortably in long range.
        public long entry_amount;
        public long match_id;
        public long created_at;
        public string status;
        // Server assignment (populated on accept)
        public string server_ip;
        public int server_port;
    }

    /// <summary>Total challenges in store (all statuses).</summary>
    public static int TotalCount
    {
        get
        {
            var data = ReadStore();
            return data.challenges.Count;
        }
    }

    /// <summary>
    /// Register a challenge (called after on-chain create_game succeeds).
    /// Returns server assignment so the creator can connect immediately.
    /// </summary>
    public static BackendClient.ServerAssignment RegisterChallenge(string gamePda, string creatorPubkey, ulong entryAmount, ulong matchId)
    {
        var data = ReadStore();

        // Remove existing entry for same PDA if any
        data.challenges.RemoveAll(c => c.game_pda == gamePda);

        data.challenges.Add(new ChallengeEntry
        {
            game_pda = gamePda,
            creator_pubkey = creatorPubkey,
            entry_amount = (long)entryAmount,
            match_id = (long)matchId,
            created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            status = "created_on_chain",
            server_ip = EDITOR_SERVER_IP,
            server_port = (int)EDITOR_SERVER_PORT
        });

        WriteStore(data);

        Debug.Log($"[LocalChallengeStore] Registered: {gamePda[..8]}... wager={(entryAmount / 1_000_000_000.0):F4} SOL -> {EDITOR_SERVER_IP}:{EDITOR_SERVER_PORT}  (total={data.challenges.Count})");

        return new BackendClient.ServerAssignment
        {
            server_ip = EDITOR_SERVER_IP,
            server_port = EDITOR_SERVER_PORT,
            status = "created"
        };
    }

    /// <summary>
    /// Get all open challenges (status == "created_on_chain").
    /// </summary>
    public static List<BackendClient.ChallengeInfo> GetOpenChallenges()
    {
        var data = ReadStore();

        var open = data.challenges
            .Where(c => c.status == "created_on_chain")
            .OrderByDescending(c => c.created_at)
            .Select(c => new BackendClient.ChallengeInfo
            {
                game_pda = c.game_pda,
                creator_pubkey = c.creator_pubkey,
                entry_amount = (ulong)c.entry_amount,
                match_id = (ulong)c.match_id,
                created_at = c.created_at,
                status = c.status
            })
            .ToList();

        Debug.Log($"[LocalChallengeStore] GetOpenChallenges: {open.Count} open / {data.challenges.Count} total");
        return open;
    }

    /// <summary>
    /// Accept a challenge. Marks it as matched and assigns the editor server (127.0.0.1:7777).
    /// </summary>
    public static BackendClient.ServerAssignment AcceptChallenge(string gamePda, string acceptorPubkey)
    {
        var data = ReadStore();

        var challenge = data.challenges.FirstOrDefault(c => c.game_pda == gamePda);
        if (challenge == null)
        {
            Debug.LogWarning($"[LocalChallengeStore] Challenge not found: {gamePda}");
            return null;
        }

        if (challenge.status != "created_on_chain")
        {
            Debug.LogWarning($"[LocalChallengeStore] Challenge already accepted: {gamePda}");
            return null;
        }

        // Mark as matched
        challenge.status = "matched";
        challenge.server_ip = EDITOR_SERVER_IP;
        challenge.server_port = (int)EDITOR_SERVER_PORT;

        WriteStore(data);

        Debug.Log($"[LocalChallengeStore] Accepted: {gamePda[..8]}... by {acceptorPubkey[..8]}... -> {EDITOR_SERVER_IP}:{EDITOR_SERVER_PORT}");

        return new BackendClient.ServerAssignment
        {
            server_ip = EDITOR_SERVER_IP,
            server_port = EDITOR_SERVER_PORT,
            status = "matched"
        };
    }

    /// <summary>
    /// Poll challenge status (for creator waiting for opponent).
    /// </summary>
    public static BackendClient.ChallengeStatus GetChallengeStatus(string gamePda)
    {
        var data = ReadStore();

        var challenge = data.challenges.FirstOrDefault(c => c.game_pda == gamePda);
        if (challenge == null)
            return null;

        return new BackendClient.ChallengeStatus
        {
            status = challenge.status,
            server_ip = challenge.server_ip,
            server_port = (ushort)challenge.server_port
        };
    }

    /// <summary>
    /// Clear all challenges (call manually if needed).
    /// </summary>
    public static void Clear()
    {
        Debug.Log("[LocalChallengeStore] Clear() called.");
        WriteStore(new StoreData());
    }

    // ── File I/O helpers ──

    static StoreData ReadStore()
    {
        try
        {
            if (!File.Exists(STORE_FILE))
                return new StoreData();

            string json = File.ReadAllText(STORE_FILE);
            if (string.IsNullOrWhiteSpace(json))
                return new StoreData();

            var data = JsonUtility.FromJson<StoreData>(json);
            if (data == null) return new StoreData();

            // Auto-purge challenges older than 30 minutes
            long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1800;
            int before = data.challenges.Count;
            data.challenges.RemoveAll(c => c.created_at < cutoff);
            if (data.challenges.Count < before)
            {
                Debug.Log($"[LocalChallengeStore] Purged {before - data.challenges.Count} stale challenges");
                WriteStore(data);
            }

            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LocalChallengeStore] Failed to read store: {e.Message}");
            return new StoreData();
        }
    }

    static void WriteStore(StoreData data)
    {
        try
        {
            // Ensure Temp directory exists
            string dir = Path.GetDirectoryName(STORE_FILE);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(data, true);

            // Atomic write: write to temp file, then rename
            string tempFile = STORE_FILE + ".tmp";
            File.WriteAllText(tempFile, json);
            if (File.Exists(STORE_FILE))
                File.Delete(STORE_FILE);
            File.Move(tempFile, STORE_FILE);
        }
        catch (Exception e)
        {
            Debug.LogError($"[LocalChallengeStore] Failed to write store: {e.Message}");
        }
    }
}
#endif
