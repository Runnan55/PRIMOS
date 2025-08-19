using System.Collections.Generic;
using System.Linq;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameStatistic : NetworkBehaviour
{
    private readonly SyncList<PlayerInfo> players = new SyncList<PlayerInfo>();

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
        public bool isAlive;

        public PlayerInfo(string name, int kills, int bulletsReloaded, int bulletsFired, int damageDealt, int timesCovered, int customPoints, bool isDisconnected = false, int deathOrder = 0, bool isAlive = true)
        {
            playerName = name;
            this.kills = kills;
            this.bulletsReloaded = bulletsReloaded;
            this.bulletsFired = bulletsFired;
            this.damageDealt = damageDealt;
            this.timesCovered = timesCovered;
            this.isDisconnected = isDisconnected;
            this.deathOrder = deathOrder;
            this.isAlive = isAlive;
            points = customPoints;
        }

        public static int CalculatePoints(int kills, int bulletsReloaded, int bulletsFired, int damageDealt, int timesCovered)
        {
            int points = 0;
            points += kills * 100;
            points += (bulletsReloaded + bulletsFired + damageDealt + timesCovered) * 5;
            return points;
        }
    }

    public static int GetRankedPointsByPosition(int position)
    {
        switch (position)
        {
            case 1: return 50;
            case 2: return 25;
            case 3: return 10;
            case 4: return -10;
            case 5: return -25;
            case 6: return -50;
            default: return -50;
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

        var gm = GetComponent<GameManager>();
        var match = MatchHandler.Instance?.GetMatch(gm.matchId);
        bool isRanked = match != null && match.mode == "Ranked";

        var rankedList = playerList
            .Where(p => p != null)
            .OrderByDescending(p => p.isAlive) // El vivo primero
            .ThenByDescending(p => p.deathOrder)
            .ToList();

        for (int i = 0; i < rankedList.Count; i++)
        {
            var p = rankedList[i];
            int rankedPosition = i + 1;

            int customPoints = isRanked
                ? GetRankedPointsByPosition(rankedPosition) + (p.kills * 5)
                : PlayerInfo.CalculatePoints(p.kills, p.bulletsReloaded, p.bulletsFired, p.damageDealt, p.timesCovered);

            players.Add(new PlayerInfo(
                p.playerName,
                p.kills,
                p.bulletsReloaded,
                p.bulletsFired,
                p.damageDealt,
                p.timesCovered,
                customPoints,
                false,
                p.deathOrder,
                p.isAlive
            ));
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
                int updatedDeathOrder = player.deathOrder;
                int customPoints = PlayerInfo.CalculatePoints(
                    player.kills, player.bulletsReloaded, player.bulletsFired, player.damageDealt, player.timesCovered);

                bool preservedDisconnected = players[i].isDisconnected || disconnected; // conservar si ya estaba marcado

                players[i] = new PlayerInfo(
                    player.playerName,
                    player.kills,
                    player.bulletsReloaded,
                    player.bulletsFired,
                    player.damageDealt,
                    player.timesCovered,
                    customPoints,
                    preservedDisconnected,
                    updatedDeathOrder,
                    player.isAlive // <- ¡importante!
                );
                return;
            }
        }

        // Si aún no estaba en la lista
        int deathOrder = player.deathOrder;
        int points = PlayerInfo.CalculatePoints(
            player.kills, player.bulletsReloaded, player.bulletsFired, player.damageDealt, player.timesCovered);

        players.Add(new PlayerInfo(
            player.playerName,
            player.kills,
            player.bulletsReloaded,
            player.bulletsFired,
            player.damageDealt,
            player.timesCovered,
            points,
            disconnected,
            deathOrder,
            player.isAlive // <- ¡importante!
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

        List<PlayerInfo> copy = players.OrderByDescending(p => p.deathOrder).ToList();

        Debug.Log("[GameStatistics] === ORDEN FINAL PARA LEADERBOARD ===");
        for (int i = 0; i < copy.Count; i++)
        {
            var p = copy[i];
            Debug.Log($"#{i + 1} -> {p.playerName} | deathOrder: {p.deathOrder} | kills: {p.kills} | alive: {!p.isDisconnected}");
        }

        int count = copy.Count;
        string[] names = new string[count];
        int[] kills = new int[count];
        int[] reloaded = new int[count];
        int[] fired = new int[count];
        int[] damage = new int[count];
        int[] covered = new int[count];
        int[] points = new int[count];
        int[] orders = new int[count];
        bool[] disconnected = new bool[count];

        for (int i = 0; i < count; i++)
        {
            names[i] = copy[i].playerName;
            kills[i] = copy[i].kills;
            reloaded[i] = copy[i].bulletsReloaded;
            fired[i] = copy[i].bulletsFired;
            damage[i] = copy[i].damageDealt;
            covered[i] = copy[i].timesCovered;
            points[i] = copy[i].points;
            orders[i] = copy[i].deathOrder;
            disconnected[i] = copy[i].isDisconnected;
        }

        var playerControllers = UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

        foreach (var player in playerControllers)
        {
            if (player != null &&
                player.connectionToClient != null &&
                player.gameObject.scene == gameObject.scene)
            {
                Debug.Log($"[Server] Enviando leaderboard a {player.playerName} con NetId {player.netId}");
                player.RpcShowLeaderboard(names, kills, reloaded, fired, damage, covered, points, orders, disconnected);
            }
        }
    }
}
