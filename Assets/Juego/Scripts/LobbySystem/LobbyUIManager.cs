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

        if (CustomRoomPlayer.LocalInstance != null && !string.IsNullOrEmpty(CustomRoomPlayer.LocalInstance.currentMode))
        {
            Debug.Log("[LobbyUIManager] Lobby cargado, solicitando lista autom�ticamente...");
            RequestMatchList();
        }
    }

    void Update()
    {
        if (localPlayer == null && NetworkClient.connection?.identity != null)
        {
            localPlayer = NetworkClient.connection.identity.GetComponent<CustomRoomPlayer>();

            backToMainMenuButton.onClick.AddListener(ReturnToMainMenu);
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

    public void ReturnToMainMenu()
    {
        if (CustomRoomPlayer.LocalInstance != null)
        {
            CustomRoomPlayer.LocalInstance.CmdLeaveLobbyMode();
        }
    }

    public void RequestMatchList()
    {
        // TODO : Llamar al servidor para pedir lista de partidas
        CustomRoomPlayer.LocalInstance?.CmdRequestMatchList();
    }

    // Nuevo m�todo que recibir�s para actualizar matches
    public void UpdateMatchList(List<MatchInfo> matches)
    {
        // Borrar lista anterior
        foreach (Transform child in matchListContainer)
            Destroy(child.gameObject);

        // Instanciar nuevos �tems basados en la lista recibida
        foreach (var match in matches)
        {
            GameObject newItem = Instantiate(matchListItemPrefab, matchListContainer);
            LobbyListItemUI itemUI = newItem.GetComponent<LobbyListItemUI>();

            itemUI.Setup(match.matchId, this); // Le pas�s el ID de la sala y la referencia al LobbyUIManager
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
            UpdateRoomInfoText(); //VERIFICAR ESO POR LA PTMR POR QUE NO SE EST� LLAMANDO ME CAGO EN MIRROR Y LOS JUEGOS MULTIPLAYERS AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAHHHHH
            //Verificar bien luego que MIRDA HACE ESTE BLOQUE POR QUE NO LO ESTOY ENTENDIENDO; ME HE PERDIDO HACE MUCHO TIEMPO
        }
        else
        {
            // Si no estamos en una partida, limpiamos la lista de jugadores
            foreach (Transform child in playerListContainer)
                Destroy(child.gameObject);

            UpdateRoomInfoText(); // Tambi�n actualizamos la info del panel (aunque sea vac�a)
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

    public void UpdatePlayerList(List<CustomRoomPlayer> players, string adminId)
    {
        /*foreach (Transform child in playerListContainer)
        {
            Destroy(child.gameObject);
        }

        foreach (var player in players)
        {
            GameObject newItem = Instantiate(playerListItemPrefab, playerListContainer);
            var playerUI = newItem.GetComponent<LobbyPlayerItemUI>();
            bool showKick = (adminId == localAdminId) && (player.playerId != localPlayer.playerId);
            playerUI.Setup(
                player.playerName,
                player.isReady,
                showKick,
                this,
                player.playerId,
                adminId == player.playerId
                );
        }*/
    }

    public void UpdatePlayerListFromData(List<PlayerDataForLobby> players, string adminId)
    {
        foreach (Transform child in playerListContainer)
            Destroy(child.gameObject);

        foreach (var player in players)
        {
            GameObject newItem = Instantiate(playerListItemPrefab, playerListContainer);
            var playerUI = newItem.GetComponent<LobbyPlayerItemUI>();

            bool showKick = (adminId == localAdminId) && (player.playerId != CustomRoomPlayer.LocalInstance.playerId);
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


}