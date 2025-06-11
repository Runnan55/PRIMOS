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
        base.OnStopServer();

        MatchHandler.Instance.LeaveMatch(this);
        MatchHandler.Instance.RemoveFromMatchmakingQueue(this);

        if (!string.IsNullOrEmpty(currentMatchId))
        {
            //Esto no est´´a funcionando, pero debería, el jugador debería tener el currentMatchId en su inspector, revisar luego
        }
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
        yield return new WaitForSeconds(1f);
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
        string sceneName = mode == "Ranked" ? "LobbySceneRanked" : "LobbySceneCasual";
        Scene targetScene = SceneManager.GetSceneByName(sceneName);

        if (targetScene.IsValid())
        {
            // Mover al jugador a la escena aditiva correspondiente en el servidor
            SceneManager.MoveGameObjectToScene(gameObject, targetScene);
            Debug.Log($"[SERVER] Jugador movido a escena: {sceneName}");
        }

        // Guardar el modo actual en su SyncVar
        currentMode = mode;

        // Refrescar lista tras entrar al lobby
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
            CustomSceneInterestManager.Instance.RegisterPlayer(NetworkClient.connection, currentMatchId); // Asignar escena real
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

        // Avisar al GameManager de su escena que hay un nuevo jugador listo
        MatchInfo match = MatchHandler.Instance.GetMatch(currentMatchId);
        if (match != null)
        {
            Scene gameScene = SceneManager.GetSceneByName(match.sceneName);
            if (gameScene.IsValid())
            {
                foreach (var go in gameScene.GetRootGameObjects())
                {
                    GameManager gm = go.GetComponent<GameManager>();
                    if (gm != null)
                    {
                        gm.OnPlayerSceneReady(this);
                        break;
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

        TargetReturnToMainMenu(connectionToClient);
    }

    [TargetRpc]
    public void TargetReturnToMainMenu(NetworkConnection target)
    {
        SceneLoaderManager.Instance.LoadScene("MainScene"); // o "StartScene" si lo llamas así

        // Opcional: resetear UI o estado si lo necesitas
        Debug.Log("[CLIENT] Volviendo a menú principal...");

        // Esperar a que cargue la escena para acceder a sus objetos
        SceneManager.sceneLoaded += OnMainSceneLoaded;
    }

    private void OnMainSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "MainScene") return;

        SceneManager.sceneLoaded -= OnMainSceneLoaded;

        Debug.Log("[CLIENT] MainScene cargada, intentando mostrar GameSelectionMenu...");

        var mainLobbyUI = UnityEngine.Object.FindFirstObjectByType<MainLobbyUI>();
        if (mainLobbyUI != null)
        {
            mainLobbyUI.StartGameSelectionMenu();
            Debug.Log("[CLIENT] GameSelectionMenu activado correctamente");
        }
        else
        {
            Debug.LogWarning("[CLIENT] No se encontró MainLobbyUI en MainScene");
        }
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
}

public class PlayerDataForLobby
{
    public string playerName;
    public string playerId;
    public bool isReady;
}
