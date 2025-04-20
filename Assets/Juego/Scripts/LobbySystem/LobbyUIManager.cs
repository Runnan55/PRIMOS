using UnityEngine;
using UnityEngine.UI;
using Mirror;
using System.Collections.Generic;

public class LobbyUIManager : MonoBehaviour
{
    [Header("LobbyElements")]
    public GameObject lobbyPanel;
    public GameObject roomPanel;

    [Header("MatchList")]
    public Transform matchListContainer;
    public GameObject matchListItemPrefab;

    [Header("Buttons")]
    public Button createRoomButton;
    public Button refreshButton;
    public Button readyButton;
    public Button leaveRoomButton;

    [Header("LobbyPlayerItemUI")]
    public Transform playerListContainer;
    public GameObject playerListItemPrefab; // <-- Tu LobbyPlayerItemUI prefab

    private CustomRoomPlayer localPlayer;

    [HideInInspector] public string localAdminId;

    void Start()
    {
        createRoomButton.onClick.AddListener(CreateRoom);
        refreshButton.onClick.AddListener(RequestMatchList);
        readyButton.onClick.AddListener(ToogleReady);
        leaveRoomButton.onClick.AddListener(LeaveRoom);

        lobbyPanel.SetActive(true);
        roomPanel.SetActive(false);
    }

    void Update()
    {
        if (localPlayer == null && NetworkClient.connection?.identity != null)
        {
            localPlayer = NetworkClient.connection.identity.GetComponent<CustomRoomPlayer>();
        }
    }

    public void CreateRoom()
    { 
        if (localPlayer != null)
        {
            string newMatchId = System.Guid.NewGuid().ToString().Substring(0, 6); //ID de 6 caracteres
            localPlayer.CmdCreateMatch(newMatchId, "Casual"); // o "Ranked"
            roomPanel.SetActive(true);
            lobbyPanel.SetActive(false);
        }
    }

    public void RequestMatchList()
    { 
        // TODO : Llamar al servidor para pedir lista de partidas
    }

    public void ToogleReady()
    {
        if (localPlayer != null)
        {
            localPlayer.CmdToggleReady();
        }
    }

    public void LeaveRoom()
    {
        if (localPlayer != null)
        {
            localPlayer.CmdLeaveMatch(); //Primero avisar al server que sales

            //Ahora descargamos la escena de la room
            if (!string.IsNullOrEmpty(localPlayer.currentMatchId))
            {
                string roomSceneName = "Room_" + localPlayer.currentMatchId;
                if (SceneLoaderManager.Instance.IsSceneLoaded(roomSceneName))
                {
                    SceneLoaderManager.Instance.UnloadScene(roomSceneName);
                }
            }

            roomPanel.SetActive(false);
            lobbyPanel.SetActive(true);
        }
    }

    public void KickPlayer(string playerId)
    {
        if (localPlayer != null && localPlayer.isAdmin)
        {
            localPlayer.CmdKickPlayer(playerId);
        }
    }

    public void RefreshLobbyUI()
    {
        if (localPlayer == null) return;

        MatchInfo match = MatchHandler.Instance.GetMatch(localPlayer.currentMatchId);
        if (match != null)
        {
            UpdatePlayerList(match.players, match.admin.playerId);
        }
    }

    public void UpdatePlayerList(List<CustomRoomPlayer> players, string adminId)
    {
        foreach (Transform child in playerListContainer)
        {
            Destroy(child.gameObject);
        }

        foreach (var player in players)
        {
            GameObject newItem = Instantiate(playerListItemPrefab, playerListContainer);
            var playerUI = newItem.GetComponent<LobbyPlayerItemUI>();
            bool showKick = (adminId == localAdminId) && (player.playerId != localPlayer.playerId);
            playerUI.Setup(player.playerName, player.isReady, showKick, this, player.playerId);
        }
    }
}
