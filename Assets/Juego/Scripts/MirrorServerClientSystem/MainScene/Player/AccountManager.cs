using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class PlayerAccountData
{
    public string playerId;
    public string playerName;

    // A futuro:
    /*public string selectedSkin;
    public string selectedHat;
    public int currency;*/

    public PlayerAccountData(string id, string name)
    {
        playerId = id;
        playerName = name;
    }
}

public class AccountManager : NetworkBehaviour
{
    public static AccountManager Instance { get; private set; }

    private Dictionary<NetworkConnectionToClient, FirebaseCredentials> firebaseTokens = new();
    private Dictionary<NetworkConnectionToClient, PlayerAccountData> playerAccounts = new();
    private readonly Dictionary<string, NetworkConnectionToClient> uidToConn = new();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void RegisterPlayer(NetworkConnectionToClient conn, string playerName, string playerId)
    {
        PlayerAccountData data = new PlayerAccountData(playerId, playerName);
        playerAccounts[conn] = data;

        Debug.Log($"[AccountManager] Player registrado: {playerName} (ID {playerId})");
    }

    public void UnregisterPlayer(NetworkConnectionToClient conn)
    {
        if (playerAccounts.ContainsKey(conn))
            playerAccounts.Remove(conn);
    }

    public PlayerAccountData GetPlayerData(NetworkConnectionToClient conn)
    {
        playerAccounts.TryGetValue(conn, out var data);
        return data;
    }

    public bool HasDataFor(NetworkConnectionToClient conn)
    {
        return playerAccounts.ContainsKey(conn);
    }

    public void UpdatePlayerName(NetworkConnectionToClient conn, string newName)
    {
        if (playerAccounts.TryGetValue(conn, out var data))
        {
            data.playerName = newName;
            Debug.Log($"[AccountManager] Nombre actualizado a: {newName}");
        }
            
    }

    // Extiende el registro actual de credenciales para también indexar por UID
    public void RegisterFirebaseCredentials(NetworkConnectionToClient conn, string uid)
    {
        firebaseTokens[conn] = new FirebaseCredentials(uid);
        uidToConn[uid] = conn;
        Debug.Log($"[AccountManager] Credenciales de Firebase recibidas para {uid}");
        Debug.Log($"[AccountManager] connId guardado: {conn.connectionId}");
    }

    public bool TryGetFirebaseCredentials(NetworkConnectionToClient conn, out FirebaseCredentials creds)
    {
        creds = null;
        if (!firebaseTokens.TryGetValue(conn, out var stored)) return false;

        var age = DateTime.UtcNow - stored.receivedAt;
        if (age > TimeSpan.FromMinutes(50)) return false; // Token vencido

        creds = stored;
        return true;
    }

    #region Disconnect_Duplicate_User

    // Helper para consultar si un UID ya está en uso
    public bool IsUidInUse(string uid, out NetworkConnectionToClient existing)
    {
        return uidToConn.TryGetValue(uid, out existing);
    }

    // Limpieza integral cuando una conexión se cae
    public void RemoveConnection(NetworkConnectionToClient conn)
    {
        // borra player account si existiera
        if (playerAccounts.ContainsKey(conn))
            playerAccounts.Remove(conn);

        // borra tokens y el índice inverso
        if (firebaseTokens.TryGetValue(conn, out var creds))
        {
            if (uidToConn.TryGetValue(creds.uid, out var stored) && stored == conn)
                    uidToConn.Remove(creds.uid);
            firebaseTokens.Remove(conn);
        }
    }

    #endregion
}

public class FirebaseCredentials
{
    public string uid;
    public DateTime receivedAt;

    public FirebaseCredentials(string uid)
    {
        this.uid = uid;
        this.receivedAt = DateTime.UtcNow;
    }
}

public struct FirebaseCredentialMessage : NetworkMessage
{
    public string uid;
}


