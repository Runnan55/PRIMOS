using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;
using Mirror;
using System.Collections;
using UnityEngine.SceneManagement;
using System.Linq;

public class MatchHandler : NetworkBehaviour
{
    public static MatchHandler Instance { get; private set; }

    private Dictionary<string, MatchInfo> matches = new Dictionary<string, MatchInfo>();
    private int partidasActivas = 0;
    public int PartidasActivas => partidasActivas;

    [SerializeField] public GameManager gameManagerPrefab;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public bool CreateMatch(string matchId, string mode, CustomRoomPlayer creator)
    {
        if (matches.ContainsKey(matchId)) return false;

        MatchInfo newMatch = new MatchInfo
        {
            matchId = matchId,
            mode = mode,
            admin = creator,
            sceneName = "Room_" + matchId
        };
        newMatch.players.Add(creator);

        matches.Add(matchId, newMatch);
        partidasActivas++;

        Debug.Log($"[SERVER] Nueva partida creada. Total partidas activas: {partidasActivas}");

        creator.currentMatchId = matchId;
        creator.currentMode = mode;   // Agrega esto para que se sincronice al cliente
        creator.isAdmin = true;

        creator.RpcRefreshLobbyForAll();
        SendLobbyUIUpdateToAll(newMatch);
        RefreshMatchListForMode(mode);

        return true;
    }

    public bool JoinMatch(string matchId, CustomRoomPlayer player)
    {
        if (!matches.ContainsKey(matchId)) return false;

        MatchInfo match = matches[matchId];

        int maxPlayers = 6;
        if (match.players.Count >= maxPlayers)
        {
            Debug.LogWarning($"[SERVER] Sala {matchId} está llena. Rechazando a {player.playerName}");
            player.TargetReturnToLobbyScene(player.connectionToClient, match.mode);
            return false;
        }

        match.players.Add(player);

        player.currentMatchId = matchId;
        player.currentMode = match.mode; // Agrega esto para que al unirse también sepa el modo
        player.isAdmin = false;

        player.RpcRefreshLobbyForAll();
        SendLobbyUIUpdateToAll(match);
        RefreshMatchListForMode(match.mode); //Esta parte actualiza la info de la sala a los demas jugadores cuando entra a la partida, sino no se actualiza en los demás de 1/6 a 2/6 por ejemplo

        return true;
    }

    public MatchInfo GetMatchInfoByScene(Scene scene)
    {
        foreach (var match in matches.Values)
        {
            if (match.sceneName == scene.name)
                return match;
        }

        return null;
    }

    public void LeaveMatch(CustomRoomPlayer player)
    {
        if (string.IsNullOrEmpty(player.currentMatchId)) return;

        MatchInfo match = matches[player.currentMatchId];
        match.players.Remove(player);
        player.isAdmin = false;

        if (match.players.Count == 0)
        {
            matches.Remove(player.currentMatchId);
            partidasActivas--; // <--- restamos
            RefreshMatchListForMode(match.mode);
            Debug.Log($"[SERVER] Partida eliminada. Total partidas activas: {partidasActivas}");
        }
        else
        {
            // Si el Admin se va, asignar nuevo Admin
            if (match.admin == player)
            {
                match.admin = match.players[0];
                match.admin.isAdmin = true;
            }

            // Opcional: Actualizar la UI para los que quedan
            foreach (var p in match.players)
            {
                p.RpcRefreshLobbyForAll();
            }

            SendLobbyUIUpdateToAll(match);
        }

        player.RpcRefreshLobbyForAll(); //Actualiza la Info personal Del room al salir
        RefreshMatchListForMode(match.mode); //Esta parte actualiza la info de la sala a los demas jugadores cuando se sale de la partida, sino no se actualiza en los demás de 2/6 a 1/6 por ejemplo

        player.TargetForceReturnToLobby(player.connectionToClient);
        player.currentMatchId = null;
    }

    //Funcion para actualizar los Matchs para los jugadores de una sola sala casual, ranked, etc
    [Server]
    public void RefreshMatchListForMode(string mode)
    {
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn.identity == null) continue;

            var roomPlayer = conn.identity.GetComponent<CustomRoomPlayer>();
            if (roomPlayer == null) continue;

            // Solo enviar si están en el lobby del modo correspondiente y no en partida
            if (roomPlayer.currentMode == mode && !roomPlayer.isPlayingNow)
            {
                roomPlayer.TargetRequestMatchList(conn);
            }
        }
    }

    public List<MatchInfo> GetMatches(string mode)
    {
        List<MatchInfo> filteredMatches = new List<MatchInfo>();
        foreach (var match in matches.Values)
        {
            if (match.mode == mode && !match.isStarted)
            {
                filteredMatches.Add(match);
            }
        }
        return filteredMatches;
    }

    public MatchInfo GetMatch(string matchId)
    {
        if (matches.ContainsKey(matchId))
            return matches[matchId];
        else
            return null;
    }

    //??????????????????????????????????????????????????????????????????????????????????????????????????????????? Verificar esto y ver si se puede reducir a uno solo
    public bool AreAllPlayersReadyToStart(string matchId)
    {
        if (!matches.TryGetValue(matchId, out MatchInfo match)) return false;

        foreach (var player in match.players)
        {
            if (!player.isPlayingNow)
                return false;
        }
        return true;
    }
    public bool AreAllPlayersReady(string matchId)
    {
        if (!matches.TryGetValue(matchId, out MatchInfo match)) return false;

        foreach (var player in match.players)
        {
            if (!player.isReady)
                return false;
        }
        return true;
    }
    //??????????????????????????????????????????????????????????????????????????????????????????????????????????? Verificar esto cuanto antes

    public void CheckStartGame(string matchId)
    {
        if (!matches.ContainsKey(matchId)) return;

        MatchInfo match = matches[matchId];
        if (match.isStarted) return;

        if (AreAllPlayersReady(matchId))
        {
            Debug.Log($"[SERVER] Todos los jugadores listos en {matchId}, empezando partida.");
            match.isStarted = true;

            StartCoroutine(CreateRuntimeGameScene(match));

            foreach (var player in match.players)
            {
                player.DelayedResetReady(); //resetear ready
            }

        }
    }

    [Server]
    private IEnumerator CreateRuntimeGameScene(MatchInfo match)
    {
        string newSceneName = "GameScene_" + match.matchId;
        Scene newScene = SceneManager.CreateScene(newSceneName);

        yield return null; // Esperar un frame para asegurarnos que la escena esté lista

        // Cargar decorado base desde Resources
        GameObject template = Resources.Load<GameObject>("GameSceneTemplate");
        if (template == null)
        {
            Debug.LogError("[MatchHandler] No se encontró el prefab GameSceneTemplate en Resources.");
            yield break;
        }

        GameObject templateInstance = Instantiate(template);
        SceneManager.MoveGameObjectToScene(templateInstance, newScene);

        // Mover todos los jugadores a la escena recién creada
        foreach (var player in match.players)
        {
            SceneManager.MoveGameObjectToScene(player.gameObject, newScene);

            var identity = player.GetComponent<NetworkIdentity>();
            if (identity != null)
            {
                identity.sceneId = 0;
                NetworkServer.RebuildObservers(identity, true);
            }

            player.isPlayingNow = true;

            Debug.Log($"[MatchHandler] Player {player.name} está ahora en escena: {player.gameObject.scene.name}");
        }

        match.sceneName = newScene.name;
        Debug.Log($"[MatchHandler] Escena creada para partida {match.matchId}: {newScene.name}");

        // Notificar a los clientes para que carguen GameScene
        NotifyPlayersToLoadGameScene(match.matchId);
        TrySpawnGameManagerForMatch(match.matchId);
    }

    [Server]
    public void TrySpawnGameManagerForMatch(string matchId)
    {
        if (!matches.TryGetValue(matchId, out MatchInfo match)) return;

        Scene matchScene = SceneManager.GetSceneByName(match.sceneName);
        if (!matchScene.IsValid()) return;

        GameManager gmInstance = Instantiate(gameManagerPrefab);
        gmInstance.matchId = match.matchId;
        gmInstance.playerControllerPrefab = NetworkManager.singleton.playerPrefab;

        NetworkServer.Spawn(gmInstance.gameObject);
        SceneManager.MoveGameObjectToScene(gmInstance.gameObject, matchScene);

        Debug.Log($"[MatchHandler] GameManager spawn para {match.matchId}");
    }

    public bool AreAllPlayersInGameScene(string matchId)
    {
        if (!matches.TryGetValue(matchId, out MatchInfo match)) return false;

        foreach (var player in match.players)
        {
            if (player.gameObject.scene.name != "GameScene")
                return false;
        }
        return true;
    }

    [Server]
    public void NotifyPlayersToLoadGameScene(string matchId)
    {
        if (!matches.TryGetValue(matchId, out MatchInfo match)) return;

        foreach (var player in match.players)
        {
            // ENVÍA EL NOMBRE REAL DE LA ESCENA
            player.TargetStartGame(player.connectionToClient, match.sceneName);
        }
    }

    #region SnippetForRefreshLobbyRoomPlayerUI

    public void SendLobbyUIUpdateToAll(MatchInfo match)
    {
        List<PlayerDataForLobby> playerDataList = new();

        foreach (var p in match.players)
        {
            playerDataList.Add(new PlayerDataForLobby
            {
                playerName = p.playerName,
                playerId = p.playerId,
                isReady = p.isReady
            });
        }

        foreach (var p in match.players)
        {
            p.TargetUpdateLobbyUI(p.connectionToClient, playerDataList, match.admin.playerId);
        }
    }

    #endregion

    #region Busqueda Automática

    private Dictionary<string, List<CustomRoomPlayer>> matchQueue = new();
    private Dictionary<string, Coroutine> countdownCoroutines = new();
    private Dictionary<string, int> countdownSeconds = new();
    private const int MIN_PLAYERS_TO_START = 3;
    public const int MATCH_SIZE = 6; //Cantidad de jugadores para iniciar partida

    [Server]
    public void EnqueueForMatchmaking(CustomRoomPlayer player)
    {
        if (!matchQueue.ContainsKey(player.currentMode))
            matchQueue[player.currentMode] = new List<CustomRoomPlayer>();

        var queue = matchQueue[player.currentMode];

        if (!queue.Contains(player))
        {
            queue.Add(player);
            UpdateSearchingUIForMode(player.currentMode);

            // --- NUEVO: iniciar cuenta atrás si llegan a 3
            if (queue.Count == MIN_PLAYERS_TO_START && !countdownCoroutines.ContainsKey(player.currentMode))
            {
                countdownCoroutines[player.currentMode] = StartCoroutine(StartCountdownForMode(player.currentMode));
            }

            Debug.Log($"[Matchmaking] Jugador {player.playerName} agregado a la cola del modo {player.currentMode}. Total: {queue.Count}");

            /*if (queue.Count >= MATCH_SIZE)
            {
                List<CustomRoomPlayer> playersForMatch = queue.Take(MATCH_SIZE).ToList();
                queue.RemoveRange(0, MATCH_SIZE);
                CreateAutoMatch(playersForMatch, player.currentMode);
            }*/

            // --- NUEVO: iniciar partida inmediata si llegan a 6
            if (queue.Count == MATCH_SIZE)
            {
                if (countdownCoroutines.ContainsKey(player.currentMode))
                {
                    StopCoroutine(countdownCoroutines[player.currentMode]);
                    countdownCoroutines.Remove(player.currentMode);
                }
                CreateMatchNow(player.currentMode);
            }
        }
    }

    [Server]
    public void RemoveFromMatchmakingQueue(CustomRoomPlayer player)
    {
        if (string.IsNullOrEmpty(player.currentMode)) return;

        if (matchQueue.TryGetValue(player.currentMode, out var queue))
        {
            if (queue.Remove(player))
            {
                UpdateSearchingUIForMode(player.currentMode);

                // --- NUEVO: cancelar countdown si bajan de 3
                if (queue.Count < MIN_PLAYERS_TO_START && countdownCoroutines.ContainsKey(player.currentMode))
                {
                    StopCoroutine(countdownCoroutines[player.currentMode]);
                    countdownCoroutines.Remove(player.currentMode);
                    UpdateCountdownUIForMode(player.currentMode, -1); // Restablece a "Searching..."
                }

                Debug.Log($"[Matchmaking] Jugador {player.playerName} removido de la cola del modo {player.currentMode}.");
            }
        }
    }

    private IEnumerator StartCountdownForMode(string mode)
    {
        int seconds = 30;
        countdownSeconds[mode] = seconds;

        while (seconds > 0)
        {
            // Actualiza la UI en todos los jugadores de ese modo
            UpdateCountdownUIForMode(mode, seconds);

            yield return new WaitForSeconds(1f);
            seconds--;

            // Verifica si llegaron a 6 para empezar ya
            var queue = matchQueue[mode];
            if (queue.Count >= MATCH_SIZE)
            {
                break;
            }
            // Si bajan de 3, cortamos
            if (queue.Count < MIN_PLAYERS_TO_START)
            {
                UpdateCountdownUIForMode(mode, -1); // Reset UI
                yield break;
            }
        }

        // Si hay mínimo 3 jugadores, inicia partida
        var queueAfter = matchQueue[mode];
        if (queueAfter.Count >= MIN_PLAYERS_TO_START)
        {
            CreateMatchNow(mode);
        }
        else
        {
            UpdateCountdownUIForMode(mode, -1); // Reset UI
        }

        countdownCoroutines.Remove(mode);
    }

    private void UpdateCountdownUIForMode(string mode, int secondsLeft)
    {
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn.identity == null) continue;
            var roomPlayer = conn.identity.GetComponent<CustomRoomPlayer>();
            if (roomPlayer == null || roomPlayer.currentMode != mode) continue;

            if (secondsLeft > 0)
            {
                roomPlayer.TargetUpdateSearchingCountdown(roomPlayer.connectionToClient, secondsLeft, MATCH_SIZE);
            }
            else
            {
                roomPlayer.TargetUpdateSearchingCount(Mathf.Min(matchQueue[mode].Count, MATCH_SIZE), MATCH_SIZE);
            }
        }
    }

    [Server]
    private void CreateMatchNow(string mode)
    {
        if (!matchQueue.TryGetValue(mode, out var queue)) return;
        int playersToUse = Mathf.Min(queue.Count, MATCH_SIZE);

        List<CustomRoomPlayer> playersForMatch = queue.Take(playersToUse).ToList();
        queue.RemoveRange(0, playersToUse);

        CreateAutoMatch(playersForMatch, mode);

        UpdateCountdownUIForMode(mode, -1); // Reset UI
    }


    [Server]
    private void UpdateSearchingUIForMode(string mode)
    {
        if (!matchQueue.TryGetValue(mode, out var queue)) return;

        int count = Mathf.Min(queue.Count, MATCH_SIZE); // No mostrar más de MATCH_SIZE

        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn.identity == null) continue;

            var roomPlayer = conn.identity.GetComponent<CustomRoomPlayer>();
            if (roomPlayer == null || roomPlayer.currentMode != mode) continue;

            roomPlayer.TargetUpdateSearchingCount(count, MATCH_SIZE);
        }
    }

    [Server]
    private void CreateAutoMatch(List<CustomRoomPlayer> players, string mode)
    {
        string matchId = System.Guid.NewGuid().ToString().Substring(0, 6);
        MatchInfo match = new MatchInfo(matchId, mode, false)
        {
            admin = players[0],
            players = new List<CustomRoomPlayer>(players),
            sceneName = "Room_" + matchId
        };

        foreach (var p in players)
        {
            p.currentMatchId = matchId;
            p.currentMode = mode;
            p.isAdmin = (p == players[0]);
        }

        matches.Add(matchId, match);
        partidasActivas++;

        Debug.Log($"[Matchmaking] Partida automática creada con ID {matchId} en modo {mode}");

        NotifyPlayersToLoadGameScene(matchId);
        StartCoroutine(CreateRuntimeGameScene(match));
    }

    #endregion
}
