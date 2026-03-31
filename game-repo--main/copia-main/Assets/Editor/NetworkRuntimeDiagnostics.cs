using System.Text;
using UnityEditor;
using UnityEngine;
using Unity.Netcode;

public static class NetworkRuntimeDiagnostics
{
    public static void Execute()
    {
        var sb = new StringBuilder();
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.Log("[NetDiag] NetworkManager.Singleton is null.");
            return;
        }

        sb.AppendLine($"[NetDiag] isPlaying={Application.isPlaying} isServer={nm.IsServer} isClient={nm.IsClient} isHost={nm.IsHost} isListening={nm.IsListening}");
        sb.AppendLine($"[NetDiag] LocalClientId={nm.LocalClientId} ConnectedClients={nm.ConnectedClients.Count} ConnectedIds={string.Join(",", nm.ConnectedClientsIds)}");

        foreach (var kvp in nm.ConnectedClients)
        {
            var clientId = kvp.Key;
            var client = kvp.Value;
            var player = client.PlayerObject;
            sb.AppendLine(player == null
                ? $"[NetDiag] Client {clientId}: PlayerObject=NULL"
                : $"[NetDiag] Client {clientId}: PlayerObject={player.name} netId={player.NetworkObjectId} owner={player.OwnerClientId} spawned={player.IsSpawned}");
        }

        if (nm.SpawnManager != null)
        {
            sb.AppendLine($"[NetDiag] SpawnedObjects={nm.SpawnManager.SpawnedObjectsList.Count}");
            foreach (var spawned in nm.SpawnManager.SpawnedObjectsList)
            {
                if (spawned == null) continue;
                sb.AppendLine($"[NetDiag] Spawned: {spawned.name} netId={spawned.NetworkObjectId} owner={spawned.OwnerClientId} isPlayer={spawned.IsPlayerObject}");
            }

            var localPlayer = nm.SpawnManager.GetLocalPlayerObject();
            sb.AppendLine(localPlayer == null
                ? "[NetDiag] LocalPlayerObject=NULL"
                : $"[NetDiag] LocalPlayerObject={localPlayer.name} netId={localPlayer.NetworkObjectId}");
        }

        var matchManager = Object.FindFirstObjectByType<MatchManager>();
        if (matchManager != null)
        {
            sb.AppendLine($"[NetDiag] MatchManager state={matchManager.CurrentState}");
        }
        else
        {
            sb.AppendLine("[NetDiag] MatchManager missing");
        }

        var fallback = Object.FindFirstObjectByType<ServerPlayerObjectFallbackSpawner>();
        sb.AppendLine(fallback != null ? "[NetDiag] ServerPlayerObjectFallbackSpawner present" : "[NetDiag] ServerPlayerObjectFallbackSpawner missing");

        var directConnect = Object.FindFirstObjectByType<DirectConnectDevSession>();
        sb.AppendLine(directConnect != null ? "[NetDiag] DirectConnectDevSession present" : "[NetDiag] DirectConnectDevSession missing");

        Debug.Log(sb.ToString());
    }
}
