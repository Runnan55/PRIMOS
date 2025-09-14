using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameStatistic : NetworkBehaviour
{
    [Header("GameManager")]
    [SerializeField] private GameManager gameManager;

    private readonly SyncList<PlayerInfo> players = new SyncList<PlayerInfo>();

    public struct PlayerInfo
    {
        public string uid;
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

        public PlayerInfo(string uid, string name, int kills, int bulletsReloaded, int bulletsFired, int damageDealt, int timesCovered, int customPoints, bool isDisconnected = false, int deathOrder = 0, bool isAlive = true)
        {
            this.uid = uid;
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
            LogWithTime.Log($"{p.playerName}: {p.kills} kills, {p.points} puntos.");
        }
    }

    [Server]
    public void Initialize(List<PlayerController> playerList, bool isFinal = false)
    {
        // 0) Prior snapshot (kept disconnected players)
        var prev = players.ToList();
        var prevByName = prev.ToDictionary(p => p.playerName, p => p);
        players.Clear();

        // 1) Is Ranked?
        var gm = gameManager;
        bool isRanked = false;

        if (gm != null && !string.IsNullOrEmpty(gm.mode))
            isRanked = gm.mode.Equals("Ranked", StringComparison.OrdinalIgnoreCase);

        if (!isRanked)
        {
            var match = MatchHandler.Instance?.GetMatch(gm != null ? gm.matchId : string.Empty);
            if (match != null && match.mode == "Ranked")
                isRanked = true;
        }

        // 2) Union: incoming controllers + previous disconnected rows
        var allowedUids = new HashSet<string>();
        var allowedNetIds = new HashSet<uint>();
        if (gm != null)
        {
            // read-only getters you expusiste
            foreach (var u in gm.GetStartingHumans()) allowedUids.Add(u);
            foreach (var n in gm.GetStartingNetIds()) allowedNetIds.Add(n);
        }


        var rows = new List<PlayerInfo>();
        var seen = new HashSet<string>();

        // 2.a) From current PlayerControllers
        foreach (var pc in playerList.Where(p => p != null))
        {
            bool isDiscNow = (!pc.isAlive && pc.connectionToClient == null && !pc.isBot);

            string uid = !string.IsNullOrEmpty(pc.firebaseUID)
                ? pc.firebaseUID
                : (pc.ownerRoomPlayer != null ? pc.ownerRoomPlayer.firebaseUID : null);

            bool allowByNet = allowedNetIds.Count == 0 || allowedNetIds.Contains(pc.netId);
            bool allowByUid = !string.IsNullOrEmpty(uid) && allowedUids.Contains(uid);

            if (!allowByNet && !allowByUid) continue;

            rows.Add(new PlayerInfo(
                uid,
                pc.playerName,
                pc.kills,
                pc.bulletsReloaded,
                pc.bulletsFired,
                pc.damageDealt,
                pc.timesCovered,
                0,                  // points calculated later
                isDiscNow,
                pc.deathOrder,
                pc.isAlive
            ));

            var key = string.IsNullOrEmpty(uid) ? pc.playerName : uid;
            seen.Add(key);
        }

        // 2.b) Add snapshot-only (already disconnected) players not present now
        foreach (var pi in prev)
        {
            var key = string.IsNullOrEmpty(pi.uid) ? pi.playerName : pi.uid;
            if (seen.Contains(key)) continue;

            // IMPORTANT: only humans that were in the starting snapshot
            if (string.IsNullOrEmpty(pi.uid) || !allowedUids.Contains(pi.uid)) continue;

            rows.Add(new PlayerInfo(
                pi.uid,
                pi.playerName, pi.kills, pi.bulletsReloaded, pi.bulletsFired,
                pi.damageDealt, pi.timesCovered, pi.points,
                pi.isDisconnected, pi.deathOrder, pi.isAlive
            ));
        }

        // 3) Final order: winner first (higher deathOrder first)
        var ordered = rows
            .OrderByDescending(r => r.isAlive).ThenByDescending(r => r.deathOrder)
            .ToList();

        // 4) Recompute points:
        // Ranked uses ONLY the placement table you asked for.
        // Casual keeps your custom formula.
        for (int i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i];
            int rankedPosition = i + 1;

            int pts = isRanked
                ? GetRankedPointsByPosition(rankedPosition) + (r.kills * 5)
                : PlayerInfo.CalculatePoints(r.kills, r.bulletsReloaded, r.bulletsFired, r.damageDealt, r.timesCovered);

            players.Add(new PlayerInfo(
                r.uid,
                r.playerName, r.kills, r.bulletsReloaded, r.bulletsFired,
                r.damageDealt, r.timesCovered, pts,
                r.isDisconnected, r.deathOrder, r.isAlive
            ));
        }

        LogWithTime.Log("[GameStatistic] Snapshot built for leaderboard with " + players.Count + " rows.");
    }


    [Server]
    public void UpdatePlayerStats(PlayerController player, bool disconnected = false)
    {
        if (player == null) return;

        // --- Detectar modo (Ranked vs Casual) ---
        bool isRanked = false;
        if (gameManager != null)
        {
            // GameManager.mode suele ser "Ranked" o "Casual"
            if (!string.IsNullOrEmpty(gameManager.mode) && gameManager.mode.Equals("Ranked", System.StringComparison.OrdinalIgnoreCase))
                isRanked = true;

            // Si hay MatchHandler con modo, úsalo como fuente de verdad
            var match = MatchHandler.Instance != null ? MatchHandler.Instance.GetMatch(gameManager.matchId) : null;
            if (match != null && !string.IsNullOrEmpty(match.mode) && match.mode.Equals("Ranked", System.StringComparison.OrdinalIgnoreCase))
                isRanked = true;
        }

        // --- Total de jugadores para calcular posición ---
        // (Durante el cierre se habrá rellenado toda la lista; en updates intermedios,
        // players.Count puede ser menor, pero al final se corrige con el snapshot final.)
        int totalPlayers = Mathf.Max(1, players.Count);

        // --- Posición desde deathOrder ---
        // deathOrder: 1 = primer muerto ... N = ganador (último en morir/asignarse)
        int rankedPosition = (player.deathOrder <= 0)
            ? totalPlayers
            : (totalPlayers - player.deathOrder + 1);

        rankedPosition = Mathf.Clamp(rankedPosition, 1, totalPlayers);

        // --- Puntos según modo ---
        int customPoints = isRanked
            ? GetRankedPointsByPosition(rankedPosition) + (player.kills * 5)
            : PlayerInfo.CalculatePoints(
                player.kills,
                player.bulletsReloaded,
                player.bulletsFired,
                player.damageDealt,
                player.timesCovered
              );

        // --- Actualizar si ya existe la entrada ---
        string uid = !string.IsNullOrEmpty(player.firebaseUID)
            ? player.firebaseUID
            : (player.ownerRoomPlayer != null ? player.ownerRoomPlayer.firebaseUID : null);

        for (int i = 0; i < players.Count; i++)
        {
            bool match =
                (!string.IsNullOrEmpty(uid) && players[i].uid == uid) ||
                (string.IsNullOrEmpty(uid) && players[i].playerName == player.playerName);

            if (match)
            {
                bool preservedDisconnected = players[i].isDisconnected || disconnected;

                players[i] = new PlayerInfo(
                    uid,
                    player.playerName,
                    player.kills,
                    player.bulletsReloaded,
                    player.bulletsFired,
                    player.damageDealt,
                    player.timesCovered,
                    customPoints,
                    preservedDisconnected,
                    player.deathOrder,
                    player.isAlive
                );
                return;
            }
        }

        // --- O agregar una nueva entrada ---
        players.Add(new PlayerInfo(
            uid,
            player.playerName,
            player.kills,
            player.bulletsReloaded,
            player.bulletsFired,
            player.damageDealt,
            player.timesCovered,
            customPoints,
            disconnected,
            player.deathOrder,
            player.isAlive
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
        LogWithTime.Log("[GameStatistic] Mostrando Leaderboard desde el servidor...");

        // antes hacía: players.OrderByDescending(p => p.deathOrder)
        List<PlayerInfo> copy = players
            .OrderByDescending(p => p.isAlive).ThenByDescending(p => p.deathOrder)
            .ToList();

        LogWithTime.Log("[GameStatistics] === ORDEN FINAL PARA LEADERBOARD ===");
        for (int i = 0; i < copy.Count; i++)
        {
            var p = copy[i];
            LogWithTime.Log($"#{i + 1} -> {p.playerName} | deathOrder: {p.deathOrder} | kills: {p.kills} | alive: {p.isAlive}");
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

        // GameStatistics.cs
        var crps = UnityEngine.Object.FindObjectsByType<CustomRoomPlayer>(FindObjectsSortMode.None);

        foreach (var crp in crps)
        {
            if (crp != null &&
                crp.connectionToClient != null &&
                crp.gameObject.scene == gameObject.scene)
            {
                LogWithTime.Log($"[Server] Enviando leaderboard a {crp.playerName} (conn {crp.connectionToClient.connectionId})");
                crp.RpcShowLeaderboard(names, kills, reloaded, fired, damage, covered, points, orders, disconnected);
            }
        }
    }

    [Server]
    public bool TryGetPointsByUid(string uid, out int points)
    {
        if (string.IsNullOrEmpty(uid))
        {
            points = 0;
            return false;
        }
        foreach (var p in players)
        {
            if (p.uid == uid)
            {
                points = p.points;
                return true;
            }
        }
        points = 0;
        return false;
    }

    private void OnDestroy()
    {
        players.Clear();
    }
}
