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
    private Dictionary<NetworkConnectionToClient, FirebaseCredentials> firebaseTokens = new();

    public static AccountManager Instance { get; private set; }

    private Dictionary<NetworkConnectionToClient, PlayerAccountData> playerAccounts = new();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }/*

    private void OnEnable()
    {
        if (NetworkServer.active)
        {
            NetworkServer.RegisterHandler<FirebaseCredentialMessage>(OnFirebaseCredentialsReceived);
        }
    }*/
    /*
    private void OnFirebaseCredentialsReceived(NetworkConnectionToClient conn, FirebaseCredentialMessage msg)
    {
        RegisterFirebaseCredentials(conn, msg.uid);
        Debug.Log("[AccountManager] Credenciales recibidas para jugador: " + msg.uid);
    }
    */
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
    
    public void RegisterFirebaseCredentials(NetworkConnectionToClient conn, string uid)
    {
        firebaseTokens[conn] = new FirebaseCredentials(uid);
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


