using Mirror;
using UnityEngine;
using System;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

public class CustomRoomPlayer : NetworkBehaviour
{
    public static CustomRoomPlayer LocalInstance { get; private set; }

    [SyncVar] public string playerName;
    [SyncVar] public bool isReady = false;
    [SyncVar(hook = nameof(OnAdminStatusChanged))]public bool isAdmin = false;
    [SyncVar] public string playerId;
    [SyncVar] public bool isPlayingNow;

    [SyncVar(hook = nameof(OnMatchIdChanged))]
    public string currentMatchId;

    [SyncVar(hook = nameof(OnModeChanged))]
    public string currentMode; // "Casual" o "Ranked"

    // (Opcional) nombre de sala y si es privada
    [SyncVar(hook = nameof(OnRoomNameChanged))]
    public string roomName;

    [SyncVar(hook = nameof(OnPrivacyChanged))]
    public bool isPrivateRoom;

    public static event Action OnRoomDataUpdated; // Evento que llamaremos

    public GameObject loadingCanvas;

    //Información relevante para Firebase
    [SyncVar] public string firebaseUID;

    [Header("Ticket y Llaves sincronizados")]

    [SyncVar] public int syncedTickets;
    [SyncVar] public int syncedKeys;

    #region ConectWithClient

    public PlayerController linkedPlayerController;

    #endregion

    #region SincronizeUI

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        LocalInstance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log($"[CustomRoomPlayer] Soy el LocalPlayer en lobby. Mi ID es {playerId}");
    }

    //Usar sismpre on StopClient para lógica frontEnd (UI,Elementos,Botones,AudioManager, referencias locales)
    public override void OnStopClient()
    {
        base.OnStopClient();

        if (isLocalPlayer)
        {
            Debug.Log("[CustomRoomPlayer] Cliente detenido, destruyendo instancia local.");
            LocalInstance = null;
            Destroy(gameObject);
        }
    }

    //Usar siempre para lógica backEnd (Actualizar listas de server, cerrar GameScenes, abandonar partidas,etc)
    public override void OnStopServer()
    {
        // 1) Marcar como "muerto/desconectado" en su GameManager
        if (!string.IsNullOrEmpty(currentMatchId))
        {
            var match = MatchHandler.Instance.GetMatch(currentMatchId);
            if (match != null)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(match.sceneName);
                if (scene.IsValid())
                {
                    GameManager gm = null;
                    foreach (var go in scene.GetRootGameObjects())
                    {
                        gm = go.GetComponent<GameManager>();
                        if (gm != null) break;
                    }

                    if (gm != null)
                    {
                        // Asegurar el PlayerController (por si el link está nulo)
                        var pc = linkedPlayerController;
                        if (pc == null)
                        {
                            var pcs = scene.GetRootGameObjects()
                                           .SelectMany(g => g.GetComponentsInChildren<PlayerController>(true));
                            pc = pcs.FirstOrDefault(p => p.playerId == playerId);
                        }

                        if (pc != null)
                            gm.MarkDisconnected(pc);   // clave
                    }
                }
            }
        }

        // 2) Tu limpieza actual
        base.OnStopServer();
        MatchHandler.Instance.LeaveMatch(this);
        MatchHandler.Instance.RemoveFromMatchmakingQueue(this);

        Debug.Log($"[SERVER] CustomRoomPlayer desconectado: {playerName}");
    }


    private void OnAdminStatusChanged(bool oldValue, bool newValue)
    {
        OnRoomDataUpdated?.Invoke();

        if (isLocalPlayer)
        {
            CmdRequestLobbyRefresh();
        }
    }

    [Command]
    private void CmdRequestLobbyRefresh()
    {
        if (string.IsNullOrEmpty(currentMatchId)) return;

        MatchInfo match = MatchHandler.Instance.GetMatch(currentMatchId);
        if (match != null)
        {
            MatchHandler.Instance.SendLobbyUIUpdateToAll(match);
        }
    }

    private void OnMatchIdChanged(string oldId, string newId)
    {
        OnRoomDataUpdated?.Invoke();
    }

    private void OnModeChanged(string oldMode, string newMode)
    {
        OnRoomDataUpdated?.Invoke();
    }

    private void OnRoomNameChanged(string oldName, string newName)
    {
        OnRoomDataUpdated?.Invoke();
    }

    private void OnPrivacyChanged(bool oldPriv, bool newPriv)
    {
        OnRoomDataUpdated?.Invoke();
    }

    #endregion

    public void DelayedResetReady()
    {
        StartCoroutine(ResetReadyAfterDelay());
    }

    private IEnumerator ResetReadyAfterDelay()
    {
        yield return new WaitForSecondsRealtime(1f);
        isReady = false;
    }

    private void OnDestroy()
    {
        if (LocalInstance == this)
        {
            LocalInstance = null;
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        playerId = System.Guid.NewGuid().ToString(); // Genera un ID único
    }

    [Command]
    public void CmdSetPlayerName(string name)
    {
        playerName = name;
    }

    [Command]
    public void CmdSetReady()
    {
        isReady = !isReady;
        Debug.Log($"[SERVER] {playerName} está {(isReady ? "LISTO" : "NO LISTO")}");

        RpcRefreshLobbyForAll();
    }

    [Command]
    public void CmdRequestJoinLobbyScene(string mode)
    {
        if (mode == "Ranked")
        {
            if (syncedTickets <= 0)
            {
                Debug.LogWarning("[CustomRoomPlayer] Sin tickets locales. Rechazando entrada.");
                TargetReturnToMainMenu(connectionToClient);
                return;
            }

            // Permitir entrada inmediata
            MoveToLobbyScene(mode);

            // Validación remota (seguridad fuerte)
            if (AccountManager.Instance.TryGetFirebaseCredentials(connectionToClient, out var creds))
            {
                string uid = creds.uid;

                StartCoroutine(FirebaseServerClient.CheckTicketAvailable(uid, hasTicket =>
                {
                    if (!hasTicket)
                    {
                        Debug.LogWarning("[CustomRoomPlayer] Ticket desincronizado. Expulsando.");
                        TargetReturnToMainMenu(connectionToClient);
                    }
                }));

                //StartCoroutine(DelayedTicketValidation(uid, connectionToClient));
            }
        }
        // Si es modo casual pasa directo
        else
        {
            MoveToLobbyScene(mode);
        }
    }

    [Command]
    public void CmdSyncWalletFromClient(int tickets, int keys)
    {
        syncedTickets = tickets;
        syncedKeys = keys;
        Debug.Log($"[CustomRoomPlayer] Wallet sync recibida: tickets={tickets}, keys={keys}");
    }

    private void MoveToLobbyScene(string mode)
    {
        string sceneName = mode == "Ranked" ? "LobbySceneRanked" : "LobbySceneCasual";
        Scene targetScene = SceneManager.GetSceneByName(sceneName);

        if (targetScene.IsValid())
        {
            SceneManager.MoveGameObjectToScene(gameObject, targetScene);
            Debug.Log($"[SERVER] Jugador movido a escena: {sceneName}");
        }

        currentMode = mode;
        TargetRequestMatchList(connectionToClient);
    }

    [TargetRpc]
    public void TargetRequestMatchList(NetworkConnection target)
    {
        CmdRequestMatchList();
    }

    #region crearPartidas

    [Command]
    public void CmdCreateMatch(string matchId, string mode)
    {
        if (MatchHandler.Instance.CreateMatch(matchId, mode, this))
        {
            RpcRefreshLobbyForAll();
        }
    }

    [Command]
    public void CmdJoinMatch(string matchId)
    {
        if (MatchHandler.Instance.JoinMatch(matchId, this))
        {
            RpcRefreshLobbyForAll();
        }
    }

    [Command]
    public void CmdLeaveMatch()
    {
        MatchHandler.Instance.LeaveMatch(this);
    }

    [Command]
    public void CmdToggleReady()
    {
        isReady = !isReady;
        //MatchHandler.Instance.CheckStartGame(currentMatchId);
        MatchInfo match = MatchHandler.Instance.GetMatch(currentMatchId);
        if (match != null)
        {
            MatchHandler.Instance.CheckStartGame(currentMatchId);
            MatchHandler.Instance.SendLobbyUIUpdateToAll(match);
        }
        RpcRefreshLobbyForAll();
    }

    [TargetRpc]
    public void TargetForceReturnToLobby(NetworkConnection target)
    {
        LobbyUIManager ui = FindFirstObjectByType<LobbyUIManager>();
        if (ui != null)
        {
            ui.ShowLobbyPanel();       // Mostrar vista de partidas
            ui.RequestMatchList();     // Forzar actualizar la lista

            // Limpiar también la lista de jugadores del panel de sala
            ui.UpdatePlayerListFromData(new List<PlayerDataForLobby>(), ""); // Vaciar lista visual, con esto tmb eliminamos toda configuración previa como ADMIN
        }

        Debug.Log("Te expulsaron CheBoludo, andás queriendo hacerte el graciocete eh pelotudo?");
    }

    [TargetRpc]
    public void TargetStartGame(NetworkConnectionToClient conn, string sceneName)
    {
        // SIEMPRE cargar la plantilla en cliente
        string clientSceneName = "GameScene";

        if (SceneManager.GetActiveScene().name != clientSceneName)
        {
            Debug.Log($"[CLIENT] Cargando escena (plantilla): {clientSceneName}");
            currentMatchId = sceneName; // GUARDAMOS la partida real (ejemplo "GameScene_7cddf7")

            if (loadingCanvas != null)
            {
                loadingCanvas.SetActive(true);
            }

            SceneManager.sceneLoaded += OnGameSceneLoaded;
            SceneLoaderManager.Instance.LoadScene(clientSceneName);
        }
    }

    private void OnGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnGameSceneLoaded;

        Debug.Log("[CLIENT] GameScene cargada -> Registrando en CustomSceneInterestManager");

        if (isLocalPlayer)
        {
            //CustomSceneInterestManager.Instance.RegisterPlayer(NetworkClient.connection, currentMatchId); // Asignar escena real
            CmdRegisterSceneInterest(currentMatchId);
        }

        CmdNotifySceneReady();
    }

    [Command]
    private void CmdNotifySceneReady()
    {
        isPlayingNow = true;

        var identity = GetComponent<NetworkIdentity>();
        if (identity != null)
        {
            identity.sceneId = 0;

            NetworkServer.RebuildObservers(identity, true);
        }

        Debug.Log($"[SERVER] Cliente {playerName} avisó que cargó GameScene");

        // Buscar el GameManager de la partida
        MatchInfo match = MatchHandler.Instance.GetMatch(currentMatchId);
        if (match != null)
        {
            Scene gameScene = SceneManager.GetSceneByName(match.sceneName);
            if (gameScene.IsValid())
            {
                GameManager gm = null;

                foreach (var go in gameScene.GetRootGameObjects())
                {
                    gm = go.GetComponent<GameManager>();
                    if (gm != null)
                    {
                        gm.OnPlayerSceneReady(this);
                        break;
                    }
                }

                // Reconstruir observers de toda la escena para este cliente
                foreach (var go in gameScene.GetRootGameObjects())
                {
                    foreach (var netId in go.GetComponentsInChildren<NetworkIdentity>(true))
                    {
                        NetworkServer.RebuildObservers(netId, true);
                    }
                }
            }
        }
    }


    [Command]
    public void CmdKickPlayer(string targetPlayerId)
    {
        MatchInfo match = MatchHandler.Instance.GetMatch(currentMatchId);

        if (isAdmin && match != null)
        {
            // Buscar jugador por ID
            CustomRoomPlayer playerToKick = match.players.Find(p => p.playerId == targetPlayerId);

            if (playerToKick != null)
            {
                match.players.Remove(playerToKick);

                // Limpiar estado del jugador
                playerToKick.currentMatchId = null;
                playerToKick.isAdmin = false;

                // Enviar al lobby principal
                playerToKick.TargetForceReturnToLobby(playerToKick.connectionToClient);

                // Refrescar visuales para los que quedan
                MatchHandler.Instance.SendLobbyUIUpdateToAll(match);
            }
        }
    }

    [Command]
    public void CmdLeaveLobbyMode()
    {
        if (!string.IsNullOrEmpty(currentMatchId))
        {
            MatchHandler.Instance.LeaveMatch(this);
        }

        MatchHandler.Instance.RemoveFromMatchmakingQueue(this); // Quitar de la lista de busqueda automatica

        currentMode = null;

        ServerReturnToMainMenu();
    }

    // === ENTRYPOINT ÚNICO (SERVER) ===
    // Llama a esto cuando el jugador sale de la GameScene o al terminar la partida.
    [Server]
    public void ServerReturnToMainMenu()
    {
        var conn = connectionToClient;
        var im = CustomSceneInterestManager.Instance;

        // 1) Cortar flags de juego (bloquea FocusResync) y recordar escena real previa
        isPlayingNow = false;
        var oldMatchId = currentMatchId;
        currentMatchId = null;

        // 2) Destruir el PlayerController de este jugador si sigue vivo
        if (linkedPlayerController != null)
        {
            // NUEVO: notificar al GameManager antes de destruir
            var gm = linkedPlayerController.GManager;
            if (gm != null) gm.MarkDisconnected(linkedPlayerController);

            if (linkedPlayerController.gameObject != null)
                NetworkServer.Destroy(linkedPlayerController.gameObject);
            linkedPlayerController = null;
        }


        // 3) Desregistrar interés y forzar HIDE de todos los objetos de la GameScene
        if (im != null && conn != null)
        {
            // Si añadiste TryGetAssignedScene/Unregister/RebuildSceneObservers en el InterestManager, úsalo aquí:
            if (im.TryGetAssignedScene(conn, out var realSceneName))
            {
                im.Unregister(conn);                // -> el CRP deja de ser observador de la GameScene
                im.RebuildSceneObservers(realSceneName, initialize: false); // -> Mirror manda HIDE
            }
        }

        // 4) Mover este CRP al MainScene (clave para que OnCheckObserver no nos vuelva a colgar a la GameScene)
        var main = SceneManager.GetSceneByName("MainScene");
        if (main.IsValid() && gameObject.scene != main)
            SceneManager.MoveGameObjectToScene(gameObject, main);

        // 5) Disparar el retorno en el cliente (limpieza visual y carga de escena)
        TargetReturnToMainMenu(conn);
    }

    // === CLIENTE ===
    [TargetRpc]
    public void TargetReturnToMainMenu(NetworkConnection target)
    {
        // Blindaje anti-resync en el cliente
        isPlayingNow = false;
        currentMatchId = null;

        // (Failsafe visual) apaga cualquier HUD de in-game que hubiese quedado renderizado
        // Nota: el PlayerController ya fue destruido en server, esto es por si la UI quedó “huérfana” 1 frame.
        foreach (var c in Resources.FindObjectsOfTypeAll<Canvas>())
        {
            // Si usas tags/nombres para HUD in-game, filtra aquí (ej.: c.tag == "InGameUI")
            if (c != null && c.gameObject != null && c.gameObject.scene.isLoaded)
            {
                // Heurística ligera: canvases activos fuera del MainScene con nombre típico de HUD
                if (c.gameObject.name.Contains("HUD") || c.gameObject.name.Contains("Game"))
                    c.gameObject.SetActive(false);
            }
        }

        // Cargar MainScene y mostrar el menú
        SceneManager.sceneLoaded -= OnMainSceneLoaded;
        SceneManager.sceneLoaded += OnMainSceneLoaded;

        SceneLoaderManager.Instance.LoadScene("MainScene");
    }

    private void OnMainSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "MainScene") return;
        SceneManager.sceneLoaded -= OnMainSceneLoaded;

        // Abre tu UI de selección/lobby
        var mainLobbyUI = UnityEngine.Object.FindFirstObjectByType<MainLobbyUI>();
        mainLobbyUI?.StartGameSelectionMenu();
    }

    [ClientRpc]
    public void RpcRefreshLobbyForAll()
    {
        LobbyUIManager ui = FindFirstObjectByType<LobbyUIManager>();
        if (ui != null)
        {
            ui.UpdateRoomInfoText();
        }
    }
    #endregion

    #region BuscarPartidas y RoomLobbyPlayer

    [Command]
    public void CmdRequestMatchList()
    {
        if (string.IsNullOrEmpty(currentMode))
        {
            Debug.LogWarning($"[SERVER] {playerName} pidió lista sin haber elegido modo.");
            TargetReceiveMatchList(connectionToClient, new List<MatchInfo>());
            return;
        }

        var matches = MatchHandler.Instance.GetMatches(currentMode);
        List<MatchInfo> matchesToSend = new List<MatchInfo>();

        foreach (var match in matches)
        {
            matchesToSend.Add(new MatchInfo
            {
                matchId = match.matchId,
                mode = match.mode,
                isStarted = match.isStarted,
                playerCount = match.players.Count
            });
        }

        TargetReceiveMatchList(connectionToClient, matchesToSend);
    }

    [TargetRpc]
    public void TargetReturnToLobbyScene(NetworkConnection target, string mode)
    {
        Debug.LogWarning("[CLIENT] No se pudo entrar: sala llena. Volviendo al lobby...");
        CmdRequestJoinLobbyScene(mode);
    }

    [TargetRpc]
    public void TargetReceiveMatchList(NetworkConnection target, List<MatchInfo> matches)
    {
        LobbyUIManager ui = FindFirstObjectByType<LobbyUIManager>();
        if (ui != null)
        {
            ui.UpdateMatchList(matches);
        }
    }

    [TargetRpc]
    public void TargetUpdateLobbyUI(NetworkConnection target, List<PlayerDataForLobby> players, string adminId)
    {
        LobbyUIManager ui = FindFirstObjectByType<LobbyUIManager>();
        if (ui != null)
        {
            ui.UpdatePlayerListFromData(players, adminId);
        }
    }

    #endregion


    #region Busqueda Automática

    [Command]
    public void CmdSearchForMatch()
    {
        if (string.IsNullOrEmpty(currentMode))
        {
            Debug.LogWarning("No se puede buscar partida sin haber elegido modo.");
            return;
        }

        Debug.Log($"[SERVER] Jugador {playerName} busca partida...");

        MatchHandler.Instance.EnqueueForMatchmaking(this);
    }

    [Command]
    public void CmdCancelSearch()
    {
        Debug.Log($"[SERVER] Jugador {playerName} cancela búsqueda.");

        MatchHandler.Instance.RemoveFromMatchmakingQueue(this);
    }

    [TargetRpc]
    public void TargetUpdateSearchingCountdown(NetworkConnection target, int secondsLeft, int maxCount)
    {
        LobbyUIManager ui = FindFirstObjectByType<LobbyUIManager>();
        if (ui != null)
        {
            ui.UpdateSearchingTextWithCountdown(secondsLeft, maxCount);
        }
    }

    [TargetRpc]
    public void TargetUpdateSearchingCount(int currentCount, int maxCount)
    {
        LobbyUIManager ui = FindFirstObjectByType<LobbyUIManager>();
        if (ui != null)
        {
            ui.UpdateSearchingText(currentCount, maxCount);
        }
    }

    #endregion

    #region Client Ranked Timer

    [TargetRpc]
    public void TargetReceiveTime(NetworkConnectionToClient target, long nowTicks, long targetTicks, bool isActiveNow)
    {
        DateTime now = new DateTime(nowTicks);
        DateTime targetTime = new DateTime(targetTicks);

        var timer = FindFirstObjectByType<ClientCountdownTimer>();
        if (timer != null)
        {
            timer.SetTimesFromServer(now, targetTime, isActiveNow);
        }
    }

    #endregion

    #region Pedir credenciales a servidor

    [Command]
    public void CmdRequestTicketAndKeyStatus()
    {
        if (!AccountManager.Instance.TryGetFirebaseCredentials(connectionToClient, out var creds))
        {
            Debug.LogWarning("[CustomRoomPlayer] No UID.");
            return;
        }

        string uid = creds.uid;
        StartCoroutine(FirebaseServerClient.FetchTicketAndKeyInfoFromWallet(creds.uid, (tickets, keys) =>
        {
            syncedTickets = tickets;
            syncedKeys = keys;

            TargetReceiveWalletData(connectionToClient, tickets, keys);
        }));
    }
    
    [TargetRpc]
    public void TargetReceiveWalletData(NetworkConnection target, int tickets, int keys)
    {
        var mainUI = FindFirstObjectByType<MainLobbyUI>();
        if (mainUI != null)
        {
            mainUI.UpdateTicketAndKeyDisplay(tickets, keys);
        }
    }
    
    #endregion

    #region Enviar o Pedir Nombre a Server

    private string GenerateDefaultNickname()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var rng = new System.Random(System.Guid.NewGuid().GetHashCode());
        var buf = new char[5];
        for (int i = 0; i < buf.Length; i ++) buf[i] = chars[rng.Next(chars.Length)];
        return "MrNobody_" + new string(buf);
    }

    [Command]
    public void CmdRequestNicknameFromFirestore()
    {
        if (AccountManager.Instance.TryGetFirebaseCredentials(connectionToClient, out var creds))
        {
            string uid = creds.uid;

            StartCoroutine(FirebaseServerClient.GetNicknameFromFirestore(uid, (nicknameInFirestore) =>
            {
                string finalName = string.IsNullOrWhiteSpace(nicknameInFirestore)
                    ? GenerateDefaultNickname()
                    : nicknameInFirestore.Trim();

                // Si estaba vacío en Firestore, lo creamos allí también
                if (string.IsNullOrWhiteSpace(nicknameInFirestore))
                {
                    StartCoroutine(FirebaseServerClient.UpdateNickname(uid, finalName));
                }


                playerName = finalName;                                   // SyncVar
                TargetReceiveNickname(connectionToClient, finalName);     // refleja en UI
            }));
        }
        else
        {
            Debug.LogWarning("[CustomRoomPlayer] No se encontró UID para conexión. Asignando un nickname temporal para no quedar vacío");
            string finalName = GenerateDefaultNickname();
            playerName = finalName;
            TargetReceiveNickname(connectionToClient, finalName);
        }
    }

    [Command]
    public void CmdUpdateNickname(string newName)
    {
        string clean = (newName ?? "").Trim();
        if (string.IsNullOrEmpty(clean))
            clean = GenerateDefaultNickname();

        playerName = clean;

        if (AccountManager.Instance.TryGetFirebaseCredentials(connectionToClient, out var creds))
        {
            StartCoroutine(FirebaseServerClient.UpdateNickname(creds.uid, clean));
        }

        // Refleja en el cliente (actualiza InputField sin disparar onValueChanged)
        TargetReceiveNickname(connectionToClient, clean);
    }

    [TargetRpc]
    public void TargetReceiveNickname(NetworkConnection target, string nickname)
    {
        var lobbyUI = FindFirstObjectByType<MainLobbyUI>();
        if (lobbyUI != null && lobbyUI.nameInputField != null)
        {
            // No dispara onValueChanged, evita bucles/guardados
            lobbyUI.nameInputField.SetTextWithoutNotify(nickname);

            // opcional: habilitar botón jugar si hay nombre
            if (lobbyUI.playButton != null)
                lobbyUI.playButton.interactable = !string.IsNullOrWhiteSpace(nickname);

            return;
        }
    }

    #endregion

    #region FocusResync resincronizar al Alt + Tab

    [Command]
    public void CmdRequestResyncObservers()
    {
        var conn = connectionToClient;
        if (conn == null || !isPlayingNow || string.IsNullOrEmpty(currentMatchId)) return;

        var im = CustomSceneInterestManager.Instance;
        var match = MatchHandler.Instance.GetMatch(currentMatchId);
        if (im == null || match == null) return;

        // 1) Reafirmar SIEMPRE el mapeo (por si el diccionario se limpió)
        im.RegisterPlayer(conn, match.sceneName);

        // 2) Ready si hiciera falta
        if (!conn.isReady) NetworkServer.SetClientReady(conn);

        // 3) Asegura que el CRP esté en la escena real de la partida
        var scene = SceneManager.GetSceneByName(match.sceneName);
        if (scene.IsValid() && gameObject.scene != scene)
            SceneManager.MoveGameObjectToScene(gameObject, scene);

        // 4) Rebuild observers de todos los NetworkIdentity de esa escena
        foreach (var ni in NetworkServer.spawned.Values.ToArray())
        {
            if (ni != null && ni.gameObject.scene == scene)
                NetworkServer.RebuildObservers(ni, initialize: false);
        }
    }

    #endregion

    #region Update LeaderboardRanked

    [Command]
    public void CmdFetchLeaderboard()
    {
        if (!AccountManager.Instance.TryGetFirebaseCredentials(connectionToClient, out var creds))
        {
            Debug.LogWarning("[SERVER] No UID para leaderboard.");
            return;
        }

        StartCoroutine(FirebaseServerClient.FetchTop100Leaderboard(creds.uid, json =>
        {
            TargetReceiveLeaderboard(connectionToClient, json);
        }));
    }

    [TargetRpc]
    public void TargetReceiveLeaderboard(NetworkConnection target, string json)
    {
        var ui = FindFirstObjectByType<MainLobbyUI>();
        ui?.OnServerLeaderboardJson(json);
    }

    [Command]
    public void CmdRegisterSceneInterest(string realSceneName)
    {
        var im = CustomSceneInterestManager.Instance;
        if (im == null) return;

        // 1) Vincula SIEMPRE esta conexión con la escena real
        im.RegisterPlayer(connectionToClient, realSceneName);

        // 2) Asegura Ready (no hagas NotReady nunca aquí)
        if (!connectionToClient.isReady)
            NetworkServer.SetClientReady(connectionToClient);

        // 3) Mueve el CustomRoomPlayer a la escena real (clave para OnCheckObserver)
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(realSceneName);
        if (scene.IsValid() && gameObject.scene != scene)
            SceneManager.MoveGameObjectToScene(gameObject, scene);

        // 4) Rebuild solo de los objetos de esa escena
        if (scene.IsValid())
        {
            foreach (var ni in Mirror.NetworkServer.spawned.Values)
            {
                if (ni != null && ni.gameObject.scene == scene)
                    Mirror.NetworkServer.RebuildObservers(ni, false);
            }
        }
    }

    #endregion
}

public class PlayerDataForLobby
{
    public string playerName;
    public string playerId;
    public bool isReady;
}
