using Mirror;
using UnityEngine;
using System;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

public class CustomRoomPlayer : NetworkBehaviour
{
    public static CustomRoomPlayer LocalInstance { get; private set; }

    [SyncVar] public string playerName;
    [SyncVar] public bool isReady = false;
    [SyncVar] public bool isAdmin = false;
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
        MatchHandler.Instance.CheckStartGame(currentMatchId);
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
        }
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

            // Solo cliente local, instanciar GameManager local
            if (GameManager.Instance == null)
            {
                GameObject gmInstance = Instantiate(NetworkManager.singleton.GetComponent<CustomNetworkManager>().gameManagerPrefab);
                //DontDestroyOnLoad(gmInstance);

                ///IMPORTANTISIMO AAAAAAAAAAAAAAAAH, VERIFICAR SI EL GAMEMANGER ES NECESARIO PARA INSTANCIAR, O SI PODEMOS ELIMINARLO EN LOS CLIENTES Y USAR SOLO EN EL SERVER CTM GAAAA
                GameManager gm = gmInstance.GetComponent<GameManager>();
                gm.matchId = currentMatchId; // <-- Asignar matchId para sincronizar la partida

                Debug.Log($"[CLIENT] GameManager local instanciado para cliente con matchId {currentMatchId}");
            }
        }

        CmdNotifySceneReady();
    }

    /*[Command]
    private void CmdNotifySceneReady()
    {
        Debug.Log($"[SERVER] Cliente {playerName} avisó que cargó GameScene");

        var identity = GetComponent<NetworkIdentity>();
        if (identity != null)
        {
            identity.sceneId = 0;
            NetworkServer.RebuildObservers(identity, true);
        }

        isPlayingNow = true;
        /*
        if (MatchHandler.Instance.AreAllPlayersReadyToStart(currentMatchId))
        {
            MatchHandler.Instance.TrySpawnGameManagerForMatch(currentMatchId);
        }
    }*/

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


    //IMPORTANTE USAR ESTO CUANDO VAYAMOS A SALIR DE UNA ESCENA POR QUE SINO LUEGO DARÄ ERRORES Y MARCARA AL JUGADOR COMO QUE SIGUE JUGANDO DE MOMENTO ESTA INACTIVO PERO HAY QUE CONECTARLO 
    //EVENTUALMENTE AAAAAAAAAAAAAAAHHHHHHHHHHHH MI PISHULAAAAAAAAAAAAAAAAAAAAAAAAAAAA
    [Server]
    public void OnLeftGame()
    {
        isPlayingNow = false;
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
                playerToKick.connectionToClient.Disconnect();

                RpcRefreshLobbyForAll();
            }
        }
    }

    [ClientRpc]
    public void RpcRefreshLobbyForAll()
    {
        LobbyUIManager ui = FindFirstObjectByType<LobbyUIManager>();
        if (ui != null)
        {
            ui.RefreshLobbyUI();
        }
    }

    [TargetRpc]
    public void TargetUpdateAdmin(string newAdminId)
    {
        LobbyUIManager ui = FindFirstObjectByType<LobbyUIManager>();
        if (ui != null)
        {
            ui.localAdminId = newAdminId;  // <- Guardás quien es el Admin en UI
            ui.RefreshLobbyUI();           // <- Refrescás botones / lista
        }
    }
    #endregion

    #region buscarPartidas

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
            matchesToSend.Add(new MatchInfo(match.matchId, match.mode, match.isStarted));
        }

        TargetReceiveMatchList(connectionToClient, matchesToSend);
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

    #endregion
}
