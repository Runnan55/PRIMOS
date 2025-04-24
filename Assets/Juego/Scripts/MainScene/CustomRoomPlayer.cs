using Mirror;
using UnityEngine;
using System;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class CustomRoomPlayer : NetworkBehaviour
{
    public static CustomRoomPlayer LocalInstance { get; private set; }

    [SyncVar] public string playerName;
    [SyncVar] public bool isReady = false;
    [SyncVar] public bool isAdmin = false;
    [SyncVar] public string playerId;

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

    #region SincronizeUI

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        LocalInstance = this;
        DontDestroyOnLoad(gameObject);
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
    public void TargetStartGame(NetworkConnectionToClient conn, string mode)
    {
        string gameSceneName = "GameScene";

        if (SceneManager.GetActiveScene().name != gameSceneName)
        {
            Debug.Log($"[CLIENT] Cambiando a escena: {gameSceneName}");
            SceneLoaderManager.Instance.LoadScene(gameSceneName);
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
        var matches = MatchHandler.Instance.GetMatches(currentMode);
        List<MatchInfo> matchesToSend = new List<MatchInfo>();

        foreach (var match in matches)
        {
            // Crear una versión limpia de MatchInfo
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
