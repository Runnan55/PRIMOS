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

    private Dictionary<NetworkConnectionToClient, PlayerAccountData> playerAccounts = new();

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

    public PlayerAccountData GetPlayerData(NetworkConnectionToClient conn)
    {
        playerAccounts.TryGetValue(conn, out var data);
        return data;
    }

    public void UnregisterPlayer(NetworkConnectionToClient conn)
    {
        if (playerAccounts.ContainsKey(conn))
            playerAccounts.Remove(conn);
    }
}

