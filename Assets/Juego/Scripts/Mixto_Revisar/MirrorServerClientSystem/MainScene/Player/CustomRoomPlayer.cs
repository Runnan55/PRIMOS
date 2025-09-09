using Mirror;
using UnityEngine;
using System;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using TMPro;
using UnityEngine.UI;

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

    [Header("LeaderboardUI")]
    public Button exitGameButton;

    [Header("Resync")]
    [SyncVar] public string currentSceneName;

    #region ConectWithClient

    public PlayerController linkedPlayerController;

    #endregion

    #region SincronizeUI

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        LocalInstance = this;
        DontDestroyOnLoad(gameObject);
        LogWithTime.Log($"[CustomRoomPlayer] Soy el LocalPlayer en lobby. Mi ID es {playerId}");

        leaderboardCanvas.SetActive(false);
        if (exitGameButton) exitGameButton.onClick.AddListener(() => OnExitLeaderboardPressed());
    }

    //Usar sismpre on StopClient para lógica frontEnd (UI,Elementos,Botones,AudioManager, referencias locales)
    public override void OnStopClient()
    {
        base.OnStopClient();

        if (isLocalPlayer)
        {
            LogWithTime.Log("[CustomRoomPlayer] Cliente detenido, destruyendo instancia local.");
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
                            var pcs = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlayerController>(true));
                            pc = pcs.FirstOrDefault(p => p.playerId == playerId);
                        }

                        if (pc != null)
                        {
                            var ni = pc.GetComponent<NetworkIdentity>();
                            if (ni && ni.connectionToClient != null) ni.RemoveClientAuthority();

                            gm.PlayerWentAfk(pc);   // clave
                        }
                    }
                }
            }
        }

        // 2) Tu limpieza actual
        base.OnStopServer();

        LogWithTime.Log($"[SERVER] CustomRoomPlayer desconectado: {playerName}");
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
        LogWithTime.Log($"[SERVER] {playerName} está {(isReady ? "LISTO" : "NO LISTO")}");

        RpcRefreshLobbyForAll();
    }

    [Command]
    public void CmdRequestJoinLobbyScene(string mode)
    {
        if (mode == "Ranked")
        {
            if (syncedTickets <= 0)
            {
                LogWithTime.LogWarning("[CustomRoomPlayer] Sin tickets locales. Rechazando entrada.");
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
                        LogWithTime.LogWarning("[CustomRoomPlayer] Ticket desincronizado. Expulsando.");
                        TargetReturnToMainMenu(connectionToClient);
                    }
                }));
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
        LogWithTime.Log($"[CustomRoomPlayer] Wallet sync recibida: tickets={tickets}, keys={keys}");
    }

    private void MoveToLobbyScene(string mode)
    {
        string sceneName = mode == "Ranked" ? "LobbySceneRanked" : "LobbySceneCasual";
        Scene targetScene = SceneManager.GetSceneByName(sceneName);

        if (targetScene.IsValid())
        {
            SceneManager.MoveGameObjectToScene(gameObject, targetScene);
            LogWithTime.Log($"[SERVER] Jugador movido a escena: {sceneName}");
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

        LogWithTime.Log("Te expulsaron CheBoludo, andás queriendo hacerte el graciocete eh pelotudo?");
    }

    [TargetRpc]
    public void TargetStartGame(NetworkConnectionToClient conn, string sceneName)
    {
        LogWithTime.Log($"[REJOINDBG][CRP.TargetStartGame] uid={firebaseUID} sceneName={sceneName} t={Time.time:F3}");

        if (loadingCanvas != null)
        {
            StartCoroutine(StartLoadingCanvasForAWhile());
        }

        // SIEMPRE cargar la plantilla en cliente
        string clientSceneName = "GameScene";

        if (SceneManager.GetActiveScene().name != clientSceneName)
        {
            LogWithTime.Log($"[CLIENT] Cargando escena (plantilla): {clientSceneName}");
            currentSceneName = sceneName; // guarda el nombre REAL de escena (GameScene_xxxx) para el Cmd

            SceneManager.sceneLoaded += OnGameSceneLoaded;
            SceneLoaderManager.Instance.LoadScene(clientSceneName);
        }
    }

    private IEnumerator StartLoadingCanvasForAWhile()
    {
        loadingCanvas.SetActive(true);

        yield return new WaitForSecondsRealtime(2f);

        loadingCanvas.SetActive(false);
    }

    private void OnGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        LogWithTime.Log($"[REJOINDBG][CRP.OnGameSceneLoaded] scene={scene.name} t={Time.time:F3}");

        SceneManager.sceneLoaded -= OnGameSceneLoaded;

        LogWithTime.Log("[CLIENT] GameScene cargada -> Registrando en CustomSceneInterestManager");

        LogWithTime.Log($"[REJOINDBG][CRP.ReadyAfterOverlay] before CmdRegisterInterest t={Time.time:F3}");
        if (isLocalPlayer)
            CmdRegisterSceneInterest(currentSceneName);

        LogWithTime.Log($"[REJOINDBG][CRP.ReadyAfterOverlay] before CmdNotifySceneReady t={Time.time:F3}");
        CmdNotifySceneReady();  // el server spawnea recién ahora
    }

    [Command]
    private void CmdNotifySceneReady()
    {
        LogWithTime.Log($"[REJOINDBG][CRP.CmdNotifySceneReady] connId={connectionToClient?.connectionId} t={Time.time:F3}");

        isPlayingNow = true;

        var identity = GetComponent<NetworkIdentity>();
        if (identity != null)
        {
            identity.sceneId = 0;

            NetworkServer.RebuildObservers(identity, true);
        }

        LogWithTime.Log($"[SERVER] Cliente {playerName} avisó que cargó GameScene");

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

                var im = CustomSceneInterestManager.Instance;
                if (im != null) im.RegisterPlayer(connectionToClient, match.sceneName);

                if (!connectionToClient.isReady)
                    NetworkServer.SetClientReady(connectionToClient);

                // 2) mover el CRP a la escena real (ayuda a OnCheckObserver)
                if (gameObject.scene != gameScene)
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject, gameScene);

                // 3) solo para este conn: rebuild de todos los NI de la escena con initialize:false
                if (linkedPlayerController != null)
                    NetworkServer.RebuildObservers(linkedPlayerController.netIdentity, true);
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
            if (gm != null) gm.ExpulsionOrQuit(linkedPlayerController);

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
            LogWithTime.LogWarning($"[SERVER] {playerName} pidió lista sin haber elegido modo.");
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
        LogWithTime.LogWarning("[CLIENT] No se pudo entrar: sala llena. Volviendo al lobby...");
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
            LogWithTime.LogWarning("No se puede buscar partida sin haber elegido modo.");
            return;
        }

        LogWithTime.Log($"[SERVER] Jugador {playerName} busca partida...");

        MatchHandler.Instance.EnqueueForMatchmaking(this);
    }

    [Command]
    public void CmdCancelSearch()
    {
        LogWithTime.Log($"[SERVER] Jugador {playerName} cancela búsqueda.");

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
            LogWithTime.LogWarning("[CustomRoomPlayer] No UID.");
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
            LogWithTime.LogWarning("[CustomRoomPlayer] No se encontró UID para conexión. Asignando un nickname temporal para no quedar vacío");
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

    /*[Command]
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

        if (linkedPlayerController != null)
            NetworkServer.RebuildObservers(linkedPlayerController.netIdentity, false);
    }*/

    [Command]
    public void CmdRequestResyncObservers()
    {
        var conn = connectionToClient;
        if (conn == null || !isPlayingNow || string.IsNullOrEmpty(currentMatchId)) return;

        var im = CustomSceneInterestManager.Instance;
        var match = MatchHandler.Instance.GetMatch(currentMatchId);
        if (im == null || match == null) return;

        // 1) afirmar interes para ESTE conn (OnCheckObserver devolvera true en su GameScene)
        im.RegisterPlayer(conn, match.sceneName);

        // 2) Ready si faltaba
        if (!conn.isReady) NetworkServer.SetClientReady(conn);

        // 3) mover el CRP a la escena real (coincidir criterio de interes)
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(match.sceneName);
        if (scene.IsValid() && gameObject.scene != scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject, scene);

        // 4) Rebuild por-identidad (API publica). Esto NO envia animaciones;
        // solo envia Spawn a quien no lo tenia. Para conexiones que ya observan, no hace nada.
        // Si prefieres, puedes iterar solo los NIs relevantes (PCs + GM),
        // pero aqui iterar todos es seguro: no resetea a otros mientras no uses ClientRpc.
        foreach (var root in scene.GetRootGameObjects())
        {
            var nis = root.GetComponentsInChildren<NetworkIdentity>(true);
            for (int i = 0; i < nis.Length; i++)
            {
                var ni = nis[i];
                if (ni == null) continue;
                NetworkServer.RebuildObservers(ni, initialize: true);
            }
        }

        // 5) Refuerzo puntual de tu PC (por si aparecio despues)
        if (linkedPlayerController != null && linkedPlayerController.netIdentity != null)
            NetworkServer.RebuildObservers(linkedPlayerController.netIdentity, initialize: true);

        // Nota MUY importante:
        // A partir de aqui, en el lado de GameManager usa SOLO TargetRpc
        // (TargetPlayButtonAnimation, TargetRefreshLocalUI, TargetForcePlayAnimation, etc.).
        // Si queda un Rpc... en SendSyncWhenReady o helpers, ese es el que pisa animaciones ajenas.
    }

    #endregion

    #region Update LeaderboardRanked

    [Command]
    public void CmdFetchLeaderboard()
    {
        if (!AccountManager.Instance.TryGetFirebaseCredentials(connectionToClient, out var creds))
        {
            LogWithTime.LogWarning("[SERVER] No UID para leaderboard.");
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
        LogWithTime.Log($"[REJOINDBG][CRP.CmdRegisterSceneInterest] connId={connectionToClient?.connectionId} scene={realSceneName} t={Time.time:F3}");
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
            if (linkedPlayerController != null)
                NetworkServer.RebuildObservers(linkedPlayerController.netIdentity, false);
        }
    }

    #endregion

    #region Leaderboard

    [SerializeField] private GameObject leaderboardCanvas;
    [SerializeField] private Transform leaderboardContent;
    [SerializeField] private GameObject leaderboardEntryPrefab;

    [ClientRpc]
    public void RpcShowLeaderboard(string[] names, int[] kills, int[] reloaded, int[] fired, int[] damage, int[] covered, int[] points, int[] orders, bool[] disconnected)
    {
        if (!isOwned) return;

        leaderboardCanvas.SetActive(true);
        ClearLeaderboard();

        int count = names.Length;

        for (int i = 0; i < count; i++)
        {
            GameObject entry = Instantiate(leaderboardEntryPrefab, leaderboardContent);
            entry.transform.SetParent(leaderboardContent, false);
            entry.transform.localScale = Vector3.one;

            var texts = entry.GetComponentsInChildren<TMP_Text>();
            if (texts.Length < 7)
            {
                LogWithTime.LogError("[Leaderboard] No se encontraron suficientes TMP_Text en el prefab.");
                continue;
            }

            string displayName = $"{i + 1}. {names[i]}" + (disconnected[i] ? " (Offline)" : "");
            texts[0].text = displayName;
            texts[1].text = kills[i].ToString();
            texts[2].text = reloaded[i].ToString();
            texts[3].text = fired[i].ToString();
            texts[4].text = damage[i].ToString();
            texts[5].text = covered[i].ToString();
            texts[6].text = points[i].ToString();
        }
    }

    private void ClearLeaderboard()
    {
        foreach (Transform child in leaderboardContent)
        {
            Destroy(child.gameObject);
        }
    }

    public void OnExitLeaderboardPressed()
    {
        CloseLeaderboardCanvas();
    }

    [Command]
    public void CmdReturnToMenuScene()
    {
        ServerReturnToMainMenu();
    }

    public void CloseLeaderboardCanvas()
    {
        leaderboardCanvas.SetActive(false);
    }

    private IEnumerator DestroyMe()
    {
        yield return new WaitForSecondsRealtime(1f);
        NetworkServer.Destroy(gameObject);
    }

    [TargetRpc]
    private void TargetReturnToMainScene(NetworkConnection target)
    {
        SceneManager.LoadScene("MainScene");
    }

    #endregion

    #region Resync

    [TargetRpc]
    public void RpcSetLinkedPc(NetworkConnection target, NetworkIdentity pcIdentity)
    {
        var pc = pcIdentity != null ? pcIdentity.GetComponent<PlayerController>() : null;
        linkedPlayerController = pc;
        LogWithTime.Log($"[CRP] Linked local PC => {(pc != null ? pc.netId.ToString() : "null")}");
    }

    #endregion
}

public class PlayerDataForLobby
{
    public string playerName;
    public string playerId;
    public bool isReady;
}
