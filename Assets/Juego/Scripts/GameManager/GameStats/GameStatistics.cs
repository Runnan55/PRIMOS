using System.Collections.Generic;
using System.Linq;
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

    private int currentDeathCount = 1;

    public struct PlayerInfo
    {
        public string playerName;
        public int kills;
        public int bulletsReloaded;
        public int bulletsFired;
        public int damageDealt;
        public int timesCovered;
        public int points;
        public bool isDisconnected;
        public int deathOrder;

        public PlayerInfo(string name, int kills, int bulletsReloaded, int bulletsFired, int damageDealt, int timesCovered, bool isDisconnected = false, int deathOrder = 0)
        {
            playerName = name;
            this.kills = kills;
            this.bulletsReloaded = bulletsReloaded;
            this.bulletsFired = bulletsFired;
            this.damageDealt = damageDealt;
            this.timesCovered = timesCovered;
            this.isDisconnected = isDisconnected;
            this.deathOrder = deathOrder;

            points = CalculatePoints(kills, bulletsReloaded, bulletsFired, damageDealt, timesCovered);
        }

        private static int CalculatePoints(int kills, int bulletsReloaded, int bulletsFired, int damageDealt, int timesCovered)
        {
            int points = 0;
            points += kills * 100;
            points += (bulletsReloaded + bulletsFired + damageDealt + timesCovered) * 5;
            return points;

        }
    }

    [Server]
    public void PrintAllStats()
    {
        foreach (var p in players)
        {
            Debug.Log($"{p.playerName}: {p.kills} kills, {p.points} puntos.");
        }
    }

    [Server]
    public void Initialize(List<PlayerController> playerList)
    {
        players.Clear();

        foreach (var player in playerList)
        {
            if (player != null)
            {
                players.Add(new PlayerInfo(
                    player.playerName,
                    player.kills,
                    player.bulletsReloaded,
                    player.bulletsFired,
                    player.damageDealt,
                    player.timesCovered));
            }
        }

        Debug.Log($"[GameStatistic] Inicializado con {players.Count} jugadores.");
    }

    [Server]
    public void UpdatePlayerStats(PlayerController player, bool disconnected = false)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].playerName == player.playerName)
            {
                int deathOrder = players[i].deathOrder;
                if ((!player.isAlive || disconnected) && deathOrder == 0)
                {
                    deathOrder = currentDeathCount++;
                }

                players[i] = new PlayerInfo(
                    player.playerName,
                    player.kills,
                    player.bulletsReloaded,
                    player.bulletsFired,
                    player.damageDealt,
                    player.timesCovered,
                    disconnected,
                    deathOrder
                );
                return;
            }
        }

        // Si no estaba, lo agregamos
        int newDeathOrder = (!player.isAlive || disconnected) ? currentDeathCount++ : 0;
        players.Add(new PlayerInfo(
            player.playerName,
            player.kills,
            player.bulletsReloaded,
            player.bulletsFired,
            player.damageDealt,
            player.timesCovered,
            disconnected,
            newDeathOrder
        ));
    }

    [Server]
    public void RemovePlayer(string playerName)
    {
        players.RemoveAll(p => p.playerName == playerName);
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

        leaderboardCanvas.SetActive(true); // Activar el Canvas
        ClearLeaderboard();

        // Ordenar jugadores antes de mostrar
        var orderedPlayers = players.OrderBy(p => p.deathOrder == 0 ? int.MinValue : p.deathOrder).ToList();

        foreach (var player in players)
        {
            GameObject entry = Instantiate(leaderboardEntryPrefab, leaderboardContent);
            entry.transform.SetParent(leaderboardContent, false);
            entry.transform.localScale = Vector3.one;

            var texts = entry.GetComponentsInChildren<TMP_Text>();
            if (texts.Length < 7)
            {
                Debug.LogError("[GameStatistic] No se encontraron suficientes TMP_Text en el prefab. Debe tener al menos 7.");
                continue;
            }

            texts[0].text = player.playerName;
            texts[1].text = player.kills.ToString();
            texts[2].text = player.bulletsReloaded.ToString();
            texts[3].text = player.bulletsFired.ToString();
            texts[4].text = player.damageDealt.ToString();
            texts[5].text = player.timesCovered.ToString();
            texts[6].text = player.points.ToString();

            string displayName = player.playerName;
            if (player.isDisconnected)
            {
                displayName += " (Offline)";
            }

            texts[0].text = displayName;
        }
    }

    [Command]
    public void CmdRequestLeaderboard()
    {
        if (isServer)
        {
            Debug.Log("[GameStatistic] Cliente solicit� mostrar Leaderboard.");
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
