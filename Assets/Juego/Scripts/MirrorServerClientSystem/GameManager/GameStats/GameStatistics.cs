using System.Collections.Generic;
using System.Linq;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameStatistic : NetworkBehaviour
{
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
        // No hacemos Clear()

        foreach (var player in playerList)
        {
            if (player == null) continue;

            // Si ya existe en la lista, lo actualizamos
            int index = players.FindIndex(p => p.playerName == player.playerName);
            if (index >= 0)
            {
                var existing = players[index];
                players[index] = new PlayerInfo(
                    player.playerName,
                    player.kills,
                    player.bulletsReloaded,
                    player.bulletsFired,
                    player.damageDealt,
                    player.timesCovered,
                    existing.isDisconnected,
                    existing.deathOrder
                );
            }
            else
            {
                players.Add(new PlayerInfo(
                    player.playerName,
                    player.kills,
                    player.bulletsReloaded,
                    player.bulletsFired,
                    player.damageDealt,
                    player.timesCovered,
                    false,
                    0
                ));
            }
        }

        Debug.Log($"[GameStatistic] Inicializado con {players.Count} jugadores (incluye desconectados).");
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

        List<PlayerInfo> copy = players.ToList();
        var playerControllers = UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

        foreach (var player in playerControllers)
        {
            if (player != null && player.connectionToClient != null)
            {
                Debug.Log($"[Server] Enviando leaderboard a {player.playerName} con NetId {player.netId}");
                player.RpcShowLeaderboard(copy);
            }
        }
    }
}
