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
        // 0) Tomar el snapshot previo (tenía a los que se desconectaron)
        var prev = players.ToList(); // copia
        var prevByName = prev.ToDictionary(p => p.playerName, p => p);
        players.Clear();

        // 1) Determinar si es Ranked
        var match = MatchHandler.Instance?.GetMatch(gameManager.matchId);
        bool isRanked = match != null && match.mode == "Ranked";

        // 2) Construir la UNIÓN: controllers vivos/muertos + entradas previas (quitados)
        var rows = new List<PlayerInfo>();
        var seen = new HashSet<string>();

        // 2.a) De los PlayerController que recibimos
        foreach (var pc in playerList.Where(p => p != null))
        {
            bool wasDiscBefore = prevByName.TryGetValue(pc.playerName, out var prevInfo) && prevInfo.isDisconnected;
            bool isDiscNow = (!pc.isAlive && pc.connectionToClient == null && !pc.isBot);

            rows.Add(new PlayerInfo(
                pc.playerName,
                pc.kills,
                pc.bulletsReloaded,
                pc.bulletsFired,
                pc.damageDealt,
                pc.timesCovered,
                0,                 // puntos se calculan más abajo
                isDiscNow,
                pc.deathOrder,
                pc.isAlive
            ));
            seen.Add(pc.playerName);
        }

        // 2.b) Agregar los que estaban en el snapshot previo y YA NO tienen PlayerController (desconectados)
        foreach (var pi in prev)
        {
            if (seen.Contains(pi.playerName)) continue;
            rows.Add(new PlayerInfo(
                pi.playerName, pi.kills, pi.bulletsReloaded, pi.bulletsFired,
                pi.damageDealt, pi.timesCovered, pi.points,
                pi.isDisconnected, pi.deathOrder, pi.isAlive
            ));
        }

        // 3) Orden FINAL: una sola cola por deathOrder (ganador arriba)
        var ordered = rows
            .OrderByDescending(r => r.deathOrder)
            .ToList();

        // 4) Recalcular puntos (Ranked = tabla + kills*5; Casual = tu fórmula)
        for (int i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i];
            int rankedPosition = i + 1;

            int pts = isRanked
                ? GetRankedPointsByPosition(rankedPosition) + (r.kills * 5)
                : PlayerInfo.CalculatePoints(r.kills, r.bulletsReloaded, r.bulletsFired, r.damageDealt, r.timesCovered);

            players.Add(new PlayerInfo(
                r.playerName, r.kills, r.bulletsReloaded, r.bulletsFired,
                r.damageDealt, r.timesCovered, pts,
                r.isDisconnected, r.deathOrder, r.isAlive
            ));
        }

        Debug.Log($"[GameStatistic] Inicializado con {players.Count} jugadores (incluye desconectados previos).");
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
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].playerName == player.playerName)
            {
                bool preservedDisconnected = players[i].isDisconnected || disconnected;

                players[i] = new PlayerInfo(
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
        Debug.Log("[GameStatistic] Mostrando Leaderboard desde el servidor...");

        // antes hacía: players.OrderByDescending(p => p.deathOrder)
        List<PlayerInfo> copy = players
            .OrderByDescending(p => p.deathOrder)  // mayor deathOrder arriba
            .ToList();

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

    [Server]
    public bool TryGetPointsForPlayer(string playerName, out int points)
    {
        // Busca la entrada ya calculada en 'players' (usa el nombre que muestras en leaderboard)
        foreach (var p in players)
        {
            if (p.playerName == playerName)
            {
                points = p.points;
                return true;
            }
        }
        points = 0;
        return false;
    }

}
