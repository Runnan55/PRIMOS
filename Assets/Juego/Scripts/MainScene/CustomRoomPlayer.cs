using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CustomRoomPlayer : NetworkBehaviour
{
    [SyncVar] public string playerName;
    [SyncVar] public bool isReady = false;
    [SyncVar] public bool isAdmin = false;
    [SyncVar] public string currentMatchId;
    [SyncVar] public string playerId;

    public static CustomRoomPlayer LocalInstance;

    private void Awake()
    {
        // Solo si es el cliente local
        if (isLocalPlayer)
        {
            if (LocalInstance != null)
            {
                Destroy(gameObject); // Ya existe uno, destruir este
                return;
            }

            LocalInstance = this;
            DontDestroyOnLoad(gameObject);
        }
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

    #region crearPartidas

    [Command]
    public void CmdCreateMatch(string matchId, string mode)
    {
        if (MatchHandler.Instance.CreateMatch(matchId, mode, this))
        {
            RpcRefreshLobbyForAll();
            LoadRoomScene(matchId);
        }
    }

    [Command]
    public void CmdJoinMatch(string matchId)
    {
        if (MatchHandler.Instance.JoinMatch(matchId, this))
        {
            RpcRefreshLobbyForAll();
            LoadRoomScene(matchId);
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
        // Podrías cargar la escena de juego aquí
        SceneLoaderManager.Instance.SwitchScene("LobbySceneCasual", "GameScene");
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

    //Funcion auxiliar para cargar escenas aditivas de sala
    private void LoadRoomScene(string matchId)
    {
        string roomSceneName = "Room_" + matchId;
        if (!SceneManager.GetSceneByName(roomSceneName).isLoaded)
        {
            Debug.Log($"[CLIENT] Cargando escena aditiva: {roomSceneName}");
            SceneManager.LoadSceneAsync(roomSceneName, LoadSceneMode.Additive);
        }
    }

    #endregion
}
