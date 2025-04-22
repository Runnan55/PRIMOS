using UnityEngine;
using UnityEngine.UI;
using Mirror;
using System.Collections.Generic;
using TMPro;

public class LobbyUIManager : MonoBehaviour
{
    [Header("UIPanels")]
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

    [Header("NombreSala")]
    public TMP_Text roomInfoText;

    void Start()
    {
        createRoomButton.onClick.AddListener(CreateRoom);
        refreshButton.onClick.AddListener(RequestMatchList);
        readyButton.onClick.AddListener(ToogleReady);
        leaveRoomButton.onClick.AddListener(LeaveRoom);
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

            ShowRoomPanel();
            RequestMatchList();
        }
    }

    public void RequestMatchList()
    {
        // TODO : Llamar al servidor para pedir lista de partidas
        CustomRoomPlayer.LocalInstance?.CmdRequestMatchList();
    }

    // Nuevo método que recibirás para actualizar matches
    public void UpdateMatchList(List<MatchInfo> matches)
    {
        // Borras la lista anterior
        foreach (Transform child in matchListContainer)
            Destroy(child.gameObject);

        // Reinstancias cada partida encontrada
        foreach (var match in matches)
        {
            GameObject newItem = Instantiate(matchListItemPrefab, matchListContainer);
            LobbyListItemUI itemUI = newItem.GetComponent<LobbyListItemUI>();
            itemUI.Setup(match.matchId, this);
        }
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

            ShowLobbyPanel();
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

        MatchInfo match = null;

        if (!string.IsNullOrEmpty(localPlayer.currentMatchId))
        {
            match = MatchHandler.Instance.GetMatch(localPlayer.currentMatchId);
        }

        if (match != null)
        {
            UpdatePlayerList(match.players, match.admin.playerId);
            UpdateRoomInfoText();
        }
        else
        {
            // Si no estamos en una partida, limpiamos la lista de jugadores
            foreach (Transform child in playerListContainer)
                Destroy(child.gameObject);

            UpdateRoomInfoText(); // También actualizamos la info del panel (aunque sea vacía)
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

    public void UpdateRoomInfoText()
    {
        if (roomInfoText == null) return;

        var localPlayer = CustomRoomPlayer.LocalInstance;
        if (localPlayer == null) return;

        string matchId = localPlayer.currentMatchId;
        string mode = localPlayer.currentMode;

        roomInfoText.text = $"<b>Modo:</b> {mode}\n<b>ID Sala:</b> {matchId}";
    }

    private void OnEnable()
    {
        CustomRoomPlayer.OnRoomDataUpdated += UpdateRoomInfoText;
    }

    private void OnDisable()
    {
        CustomRoomPlayer.OnRoomDataUpdated -= UpdateRoomInfoText;
    }

    public void ShowLobbyPanel()
    {
        lobbyPanel.SetActive(true);
        roomPanel.SetActive(false);
    }

    public void ShowRoomPanel()
    {
        lobbyPanel.SetActive(false);
        roomPanel.SetActive(true);
    }


}