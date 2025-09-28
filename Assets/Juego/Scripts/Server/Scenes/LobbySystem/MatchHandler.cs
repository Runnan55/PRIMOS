using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;
using Mirror;
using System.Collections;
using UnityEngine.SceneManagement;
using System.Linq;
using Mirror.BouncyCastle.Security;



public class MatchHandler : NetworkBehaviour
{
    public static MatchHandler Instance { get; private set; }

    private Dictionary<string, MatchInfo> matches = new Dictionary<string, MatchInfo>();
    private int partidasActivas = 0;
    public int PartidasActivas => partidasActivas;

    private readonly HashSet<string> modesStarting = new HashSet<string>();

    [SerializeField] public GameManager gameManagerPrefab;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private class ModeConfig
    {
        public int MinPlayersToStart;
        public int CountdownSeconds;
    }

    private readonly Dictionary<string, ModeConfig> modeConfig = new()
    {
        ["Casual"] = new ModeConfig { MinPlayersToStart = 1, CountdownSeconds = 40},
        ["Ranked"] = new ModeConfig { MinPlayersToStart = 1, CountdownSeconds = 30},
        // Podemos seguir añadiendo otros modos aquí sin enredarnos
    };

    private int GetMinPlayers(string mode)
        => modeConfig.TryGetValue(mode, out var cfg) ? cfg.MinPlayersToStart : 3; // Funcion de fallback final por si hay modos no configurados

    private int GetCountdownSeconds(string mode)
        => modeConfig.TryGetValue(mode, out var cfg) ? cfg.CountdownSeconds : 30; // 30 segundos de fallback por si hay modos no configurados

    #region Área Mixta : Matchmaking (Colas, Countdown, Creación)

    private Dictionary<string, List<CustomRoomPlayer>> matchQueue = new();
    private Dictionary<string, Coroutine> countdownCoroutines = new();
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

            // --- NUEVO: iniciar cuenta atrás si llegan al mínimo por mnodo
            int minToStart = GetMinPlayers(player.currentMode);
            if (queue.Count >= minToStart && !countdownCoroutines.ContainsKey(player.currentMode))
            {
                countdownCoroutines[player.currentMode] = StartCoroutine(StartCountdownForMode(player.currentMode));
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

                // --- NUEVO: cancelar countdown si bajan del mínimo por modo
                int minToStart = GetMinPlayers(player.currentMode);
                if (queue.Count < minToStart && countdownCoroutines.ContainsKey(player.currentMode))
                {
                    StopCoroutine(countdownCoroutines[player.currentMode]);
                    countdownCoroutines.Remove(player.currentMode);
                    UpdateCountdownUIForMode(player.currentMode, -1); // Restablece a "Searching..."
                }
            }
        }
    }

    private IEnumerator StartCountdownForMode(string mode)
    {
        int seconds = GetCountdownSeconds(mode);

        try
        {
            while (seconds > 0)
            {
                // Actualiza la UI en todos los jugadores de ese modo
                UpdateCountdownUIForMode(mode, seconds);

                yield return new WaitForSecondsRealtime(1f);
                seconds--;

                // Verifica si llegaron a 6 para empezar ya
                var queue = matchQueue[mode];
                if (queue.Count >= MATCH_SIZE)
                {
                    break;
                }
                // Si bajan del mínimo por modo cortamos
                if (queue.Count < GetMinPlayers(mode))
                {
                    UpdateCountdownUIForMode(mode, -1); // Reset UI
                    yield break;
                }
            }

            // Si hay el mínimo de jugadores por modo, inicia partida
            var queueAfter = matchQueue[mode];
            if (queueAfter.Count >= GetMinPlayers(mode))
            {
                CreateMatchNow(mode);
            }
            else
            {
                UpdateCountdownUIForMode(mode, -1); // Reset UI
            }
        }
        finally
        {
            countdownCoroutines.Remove(mode);

        }
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

    private void CreateMatchNow(string mode)
    {
        if (!matchQueue.TryGetValue(mode, out var queue)) return;

        int playersToUse = Mathf.Min(queue.Count, MATCH_SIZE);
        List<CustomRoomPlayer> playersForMatch = queue.Take(playersToUse).ToList();
        queue.RemoveRange(0, playersToUse);

        #region Casual/Otros : Branch directo

        if (mode != "Ranked")
        {
            CreateAutoMatch(playersForMatch, mode);
            UpdateCountdownUIForMode(mode, -1);
            
            if (matchQueue.TryGetValue(mode, out var q) &&
                q.Count >= GetMinPlayers(mode) &&
                !countdownCoroutines.ContainsKey(mode))
                {
                    countdownCoroutines[mode] = StartCoroutine(StartCountdownForMode(mode));
                }
            return;
        }

        #endregion

        #region — Ranked: pre-check a TODOS, luego consumo a TODOS —

        var expelledOnCheck = new HashSet<CustomRoomPlayer>();
        bool finished = false;

        int pendingCheck = playersForMatch.Count;
        foreach (var p in playersForMatch)
        {
            var playerRef = p; // capturar correctamente en el closure

            if (!AccountManager.Instance.TryGetFirebaseCredentials(playerRef.connectionToClient, out var creds))
            {
                expelledOnCheck.Add(playerRef);
                playerRef.TargetReturnToMainMenu(playerRef.connectionToClient);
                if (--pendingCheck == 0) AfterCheck();
                continue;
            }

            // 1) PRE-CHECK: ¿tiene ticket?
            playerRef.StartCoroutine(FirebaseServerClient.CheckTicketAvailable(creds.uid, hasTicket =>
            {
                if (!hasTicket)
                {
                    expelledOnCheck.Add(playerRef);
                    playerRef.TargetReturnToMainMenu(playerRef.connectionToClient); // expulsar
                }
                if (--pendingCheck == 0) AfterCheck();
            }));
        }

        // local: tras pre-check
        void AfterCheck()
        {
            if (finished) return;

            // Si alguien NO tenía ticket -> cancelar inicio y re-encolar a los que sí
            if (expelledOnCheck.Count > 0)
            {
                foreach (var ok in playersForMatch.Where(x => !expelledOnCheck.Contains(x)))
                    EnqueueForMatchmaking(ok); // reutilizamos tu método existente

                UpdateCountdownUIForMode(mode, -1);

                if (matchQueue.TryGetValue(mode, out var q) &&
                q.Count >= GetMinPlayers(mode) &&
                !countdownCoroutines.ContainsKey(mode))
                {
                    countdownCoroutines[mode] = StartCoroutine(StartCountdownForMode(mode));
                }
                return;
            }

            // 2) Todos pasaron pre-check -> solo verificar de nuevo que tickets > 0 (sin consumir)
            var expelledOnSecondCheck = new HashSet<CustomRoomPlayer>();
            int pendingVerify = playersForMatch.Count;

            foreach (var p in playersForMatch)
            {
                var playerRef = p;
                if (!AccountManager.Instance.TryGetFirebaseCredentials(playerRef.connectionToClient, out var creds2))
                {
                    expelledOnSecondCheck.Add(playerRef);
                    playerRef.TargetReturnToMainMenu(playerRef.connectionToClient);
                    if (--pendingVerify == 0) AfterSecondCheck();
                    continue;
                }

                playerRef.StartCoroutine(FirebaseServerClient.CheckTicketAvailable(creds2.uid, hasTicket =>
                {
                    if (!hasTicket)
                    {
                        expelledOnSecondCheck.Add(playerRef);
                        playerRef.TargetReturnToMainMenu(playerRef.connectionToClient);
                    }

                    if (--pendingVerify == 0) AfterSecondCheck();
                }));
            }

            // local: tras segunda verificación
            void AfterSecondCheck()
            {
                if (finished) return;

                // Si alguien falló -> cancelar y re-encolar a los OK
                if (expelledOnSecondCheck.Count > 0)
                {
                    foreach (var ok in playersForMatch.Where(x => !expelledOnSecondCheck.Contains(x)))
                        EnqueueForMatchmaking(ok);

                    UpdateCountdownUIForMode(mode, -1);
                    finished = true;
                    return;
                }

                // Todo OK -> arrancar la partida (tickets se cobrarán más tarde en GameManager)
                CreateAutoMatch(playersForMatch, mode);
                UpdateCountdownUIForMode(mode, -1);
                finished = true;
            }
        }
        #endregion
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

        StartCoroutine(CreateRuntimeGameScene(match));
    }

    #endregion

    #region Área Mixta : Lobby, UI & Gestión de Escenas

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
            LogWithTime.Log($"[SERVER] Partida eliminada. Total partidas activas: {partidasActivas}");
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

    public void CheckStartGame(string matchId)
    {
        if (!matches.ContainsKey(matchId)) return;

        MatchInfo match = matches[matchId];
        if (match.isStarted) return;

        if (AreAllPlayersReady(matchId))
        {
            LogWithTime.Log($"[SERVER] Todos los jugadores listos en {matchId}, empezando partida.");
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
        }

        match.sceneName = newScene.name;

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
        gmInstance.mode = match.mode; // Setea el modo de juego para que GameManager lo sepa 

        NetworkServer.Spawn(gmInstance.gameObject);
        SceneManager.MoveGameObjectToScene(gmInstance.gameObject, matchScene);
    }

    [Server]
    public void NotifyPlayersToLoadGameScene(string matchId)
    {
        if (!matches.TryGetValue(matchId, out MatchInfo match)) return;

        foreach (var player in match.players)
        {
            // ENVÍA EL NOMBRE REAL DE LA ESCENA
            player.TargetStartGame(player.connectionToClient, match.sceneName);
            LogWithTime.Log($"[TRACE] TargetStartGame(initial) -> {player.playerName} match={matchId} scene={match.sceneName}");
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

    [Server]
    public void AbortMatch(string matchId, string reason = "")
    {
        if (!matches.TryGetValue(matchId, out var match)) return;

        LogWithTime.LogWarning($"[MatchHandler] Abortando match {matchId}. Razón: {reason}");

        // 1) Enviar a todos los jugadores al MainMenu
        foreach (var rp in match.players.ToArray())
        {
            if (rp != null && rp.connectionToClient != null)
            {
                // Reset mínimo de estado de sala
                rp.isPlayingNow = false;
                rp.currentMatchId = "";
                rp.TargetReturnToMainMenu(rp.connectionToClient);
            }
        }
    }

    [Server]
    public void DestroyGameScene(string sceneName, string reason = "cleanup")
    {
        if (string.IsNullOrEmpty(sceneName)) return;

        var scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid())
        {
            LogWithTime.LogWarning($"[MatchHandler] DestroyGameScene: escena '{sceneName}' no válida.");
            return;
        }

        // Seguridad: si aún hay CRP en la escena, no destruyas
        bool hasRoomPlayers = NetworkServer.connections.Values.Any(conn =>
            conn?.identity != null &&
            conn.identity.gameObject.scene == scene &&
            conn.identity.GetComponent<CustomRoomPlayer>() != null
        );
        if (hasRoomPlayers)
        {
            LogWithTime.Log($"[MatchHandler] DestroyGameScene cancelado; aún hay CRP en '{sceneName}'.");
            return;
        }

        // Localiza el match asociado (si existe)
        MatchInfo match = null; string matchId = null;
        foreach (var kv in matches)
        {
            if (kv.Value != null && kv.Value.sceneName == scene.name)
            {
                match = kv.Value; matchId = kv.Key; break;
            }
        }

        // Limpia jugadores / referencias del match
        if (match != null)
        {
            foreach (var rp in match.players.ToArray())
            {
                if (rp == null) continue;

                // NUEVO: no expulses si el jugador ya cambió de match o no está jugando
                if (rp.currentMatchId != matchId || !rp.isPlayingNow)
                    continue;

                rp.isPlayingNow = false;
                rp.currentMatchId = null;

                if (rp.connectionToClient != null)
                    rp.TargetReturnToMainMenu(rp.connectionToClient);

                if (rp.linkedPlayerController != null && rp.linkedPlayerController.gameObject != null)
                {
                    NetworkServer.Destroy(rp.linkedPlayerController.gameObject);
                    rp.linkedPlayerController = null;
                }
            }

            matches.Remove(matchId);
            partidasActivas = Mathf.Max(0, partidasActivas - 1);
            RefreshMatchListForMode(match.mode);
        }

        // Destruye objetos de red restantes y descarga escena
        foreach (var root in scene.GetRootGameObjects())
        {
            var ni = root.GetComponent<NetworkIdentity>();
            if (ni != null && NetworkServer.spawned.ContainsKey(ni.netId))
                NetworkServer.Destroy(root);
            else
                Destroy(root);
        }

        SceneManager.UnloadSceneAsync(scene);
        Resources.UnloadUnusedAssets();

        LogWithTime.Log($"[MatchHandler] Escena '{sceneName}' destruida. Motivo: {reason}");
    }

    #endregion

    #endregion

    #region Resincronización

    [Server]
    public bool TryRejoinActiveMatchByUid(CustomRoomPlayer crp, string uid)
    {
        LogWithTime.Log($"[REJOINDBG][MH.TryRejoin] uid= {uid} crpScene={crp.gameObject.scene.name} t={Time.time:F3}");

        if (string.IsNullOrEmpty(uid) || crp == null) return false;

        // Busca una GameScene activa con un PlayerController humano con ese UID
        foreach (var kv in matches)
        {
            var match = kv.Value;
            if (match == null || string.IsNullOrEmpty(match.sceneName)) continue;

            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(match.sceneName);
            if (!scene.IsValid()) continue;

            GameManager gm = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                gm = go.GetComponent<GameManager>();
                if (gm != null) break;
            }
            if (gm == null || gm.isGameOver) continue; // no rejoin si acabo

            // Encuentra el PlayerController con ese UID
            var pcs = scene.GetRootGameObjects()
                           .SelectMany(g => g.GetComponentsInChildren<PlayerController>(true))
                           .ToList();
            var pc = pcs.FirstOrDefault(p => !p.isBot && (p.firebaseUID == uid ||
                              (p.ownerRoomPlayer != null && p.ownerRoomPlayer.firebaseUID == uid)));
            if (pc == null) continue;

            // Reenlazar al CRP
            crp.currentMatchId = match.matchId;
            crp.isPlayingNow = true;
            crp.linkedPlayerController = pc;
            pc.ownerRoomPlayer = crp;            // reasigna referencia de owner
            pc.ServerSetAfk(false);              // sale de AFK

            // Autoridad para el cliente
            var ni = pc.GetComponent<NetworkIdentity>();
            if (ni != null && crp.connectionToClient != null)
            {
                // Si otro cliente tenia autoridad, quitarsela primero (solo en server)
                if (ni.connectionToClient != null && ni.connectionToClient != crp.connectionToClient)
                    ni.RemoveClientAuthority();

                // Asignar autoridad al nuevo CRP
                ni.AssignClientAuthority(crp.connectionToClient);
            }

            // Dispara carga de plantilla cliente si está en otra escena (reusa tu TargetStartGame)
            LogWithTime.Log($"[TRACE] TargetStartGame(recover) -> {crp.playerName} scene={match.sceneName}");
            crp.TargetStartGame((NetworkConnectionToClient)crp.connectionToClient, match.sceneName);

            return true;
        }
        return false;
    }


    #endregion

    #region Salas_Legacy (Deprecated) Falta COMPLETAR AAAAAAAAAAAAAAA

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

        LogWithTime.Log($"[SERVER] Nueva partida creada. Total partidas activas: {partidasActivas}");

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

    #endregion
}

public class MatchInfo
{
    public string matchId;
    public string mode;
    public bool isStarted;
    public string sceneName;

    public CustomRoomPlayer admin;
    public List<CustomRoomPlayer> players = new List<CustomRoomPlayer>();
    public int playerCount;

    public MatchInfo() { }

    public MatchInfo(string matchId, string mode, bool isStarted)
    {
        this.matchId = matchId;
        this.mode = mode;
        this.isStarted = isStarted;
    }
}
