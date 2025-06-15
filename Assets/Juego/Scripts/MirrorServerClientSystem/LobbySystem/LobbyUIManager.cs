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
    public Button backToMainMenuButton;

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

    [Header("BuscarPartida")]
    public Button searchButton;
    public Button cancelSearchButton;
    public TMP_Text searchingText;
    public TMP_Text playerText;

    private CustomRoomPlayer localPlayer;

    [HideInInspector] public string localAdminId;

    [Header("NombreSala")]
    public TMP_Text roomInfoText;

    void Start()
    {
        AudioManager.Instance.PlayMusic("CasualSearchingGameTheme");

        createRoomButton.onClick.AddListener(CreateRoom);
        refreshButton.onClick.AddListener(RequestMatchList);
        readyButton.onClick.AddListener(ToogleReady);
        leaveRoomButton.onClick.AddListener(LeaveRoom);
        searchButton.onClick.AddListener(StartSearching);
        cancelSearchButton.onClick.AddListener(CancelSearching);

        //OldSystem
        RequestMatchList();

        //NewSystem
        StartSearching();
    }

    void Update()
    {
        if (localPlayer == null && NetworkClient.connection?.identity != null)
        {
            localPlayer = NetworkClient.connection.identity.GetComponent<CustomRoomPlayer>();

            backToMainMenuButton.onClick.AddListener(ReturnToMainMenu);
        }
    }

    #region NewSystem : Busqueda Y Cancelar Partidas

    private void StartSearching()
    {
        CustomRoomPlayer.LocalInstance?.CmdSearchForMatch(); // Ya existente

        // Cambiar UI
        searchButton.gameObject.SetActive(false);
        cancelSearchButton.gameObject.SetActive(true);
    }

    private void CancelSearching()
    {
        CustomRoomPlayer.LocalInstance?.CmdCancelSearch();

        // Restaurar UI
        searchButton.gameObject.SetActive(true);
        cancelSearchButton.gameObject.SetActive(false);
    }

    public void UpdateSearchingText(int current, int max)
    {
        playerText.text = $"Players: {current}/{max}";

        // BONUS: texto dinámico
        if (current < 3)
            searchingText.text = "Searching...";
    }

    public void UpdateSearchingTextWithCountdown(int secondsLeft, int max)
    {
        searchingText.text = $"Starting game in: {secondsLeft}";
    }

    #endregion

    #region OldSystem : Crear partidas privadas con Admin

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

    public void ReturnToMainMenu()
    {
        if (CustomRoomPlayer.LocalInstance != null)
        {
            CustomRoomPlayer.LocalInstance.CmdCancelSearch(); // <- Cancelar partida antes de irse
            CustomRoomPlayer.LocalInstance.CmdLeaveLobbyMode();
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
        // Borrar lista anterior
        foreach (Transform child in matchListContainer)
            Destroy(child.gameObject);

        // Instanciar nuevos ítems basados en la lista recibida
        foreach (var match in matches)
        {
            GameObject newItem = Instantiate(matchListItemPrefab, matchListContainer);
            LobbyListItemUI itemUI = newItem.GetComponent<LobbyListItemUI>();

            int currentPlayers = match.playerCount; // Si es la versión light
            int maxPlayers = 6;
            itemUI.Setup(match.matchId, currentPlayers, maxPlayers, this); // Le pasás el ID de la sala y la referencia al LobbyUIManager
        }
    }

    public void ToogleReady()
    {
        if (localPlayer != null)
        {
            UpdateReadyButtonVisual(!localPlayer.isReady);

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

    private void UpdateReadyButtonVisual(bool isReady)
    {
        ColorBlock colors = readyButton.colors;

        if (isReady)
        {
            colors.normalColor = Color.green;
            colors.highlightedColor = Color.green;
            readyButton.GetComponentInChildren<TMP_Text>().text = "Ready!";
        }
        else
        {
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            readyButton.GetComponentInChildren<TMP_Text>().text = "Not Ready";
        }

        readyButton.colors = colors;
    }

    public void UpdatePlayerListFromData(List<PlayerDataForLobby> players, string adminId)
    {
        foreach (Transform child in playerListContainer)
            Destroy(child.gameObject);

        foreach (var player in players)
        {
            GameObject newItem = Instantiate(playerListItemPrefab, playerListContainer);
            var playerUI = newItem.GetComponent<LobbyPlayerItemUI>();

            bool showKick = CustomRoomPlayer.LocalInstance.isAdmin && player.playerId != CustomRoomPlayer.LocalInstance.playerId;
            playerUI.Setup(player.playerName, player.isReady, showKick, this, player.playerId, adminId == player.playerId);
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
        RequestMatchList();
    }

    public void ShowRoomPanel()
    {
        lobbyPanel.SetActive(false);
        roomPanel.SetActive(true);
    }

    #endregion
}