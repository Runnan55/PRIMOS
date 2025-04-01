using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameStatistic : NetworkBehaviour
{
    [SerializeField] private GameObject leaderboardCanvas;
    [SerializeField] private Transform leaderboardContent;
    [SerializeField] private GameObject leaderboardEntryPrefab;

    // Cambiar de List<PlayerController> a SyncList<PlayerInfo>
    [SyncVar] private SyncList<PlayerInfo> players = new SyncList<PlayerInfo>();

    public struct PlayerInfo
    {
        public string playerName;
        public int kills;
        public int health;

        public PlayerInfo(string name, int kills, int health)
        {
            playerName = name;
            this.kills = kills;
            this.health = health;
        }
    }

    public void Initialize(List<PlayerController> playerList)
    {
        players.Clear();

        foreach (var player in playerList)
        {
            if (player != null)
            {
                players.Add(new PlayerInfo(player.playerName, player.kills, player.health));
            }
        }

        Debug.Log($"[GameStatistic] Inicializado con {players.Count} jugadores.");
    }

    [Server]
    public void ShowLeaderboard()
    {
        Debug.Log("[GameStatistic] Mostrando Leaderboard desde el servidor...");
        RpcShowLeaderboard();
    }

    [ClientRpc]
    private void RpcShowLeaderboard()
    {
        Debug.Log("[GameStatistic] Cliente: Activando Leaderboard Canvas...");

        if (leaderboardCanvas == null)
        {
            Debug.LogError("[GameStatistic] Leaderboard Canvas no está asignado.");
            return;
        }

        leaderboardCanvas.SetActive(true); // Activar el Canvas

        if (leaderboardEntryPrefab == null)
        {
            Debug.LogError("[GameStatistic] Leaderboard Entry Prefab no está asignado.");
            return;
        }

        if (players == null || players.Count == 0)
        {
            Debug.LogError("[GameStatistic] La lista de jugadores está vacía o no se ha inicializado.");
            return;
        }

        ClearLeaderboard();

        foreach (var player in players)
        {
            GameObject entry = Instantiate(leaderboardEntryPrefab, leaderboardContent);
            entry.transform.SetParent(leaderboardContent, false);
            entry.transform.localScale = Vector3.one;

            var texts = entry.GetComponentsInChildren<TMP_Text>();
            if (texts.Length < 3)
            {
                Debug.LogError("[GameStatistic] No se encontraron suficientes TMP_Text en el prefab. Debe tener al menos 3.");
                continue;
            }

            texts[0].text = player.playerName;
            texts[1].text = "Kills: " + player.kills.ToString();
            texts[2].text = "Health: " + player.health.ToString();
        }
    }

    [Command]
    public void CmdRequestLeaderboard()
    {
        if (isServer)
        {
            Debug.Log("[GameStatistic] Cliente solicitó mostrar Leaderboard.");
            ShowLeaderboard();
        }
    }

    private void ClearLeaderboard()
    {
        foreach (Transform child in leaderboardContent)
        {
            Destroy(child.gameObject);
        }
    }
}
