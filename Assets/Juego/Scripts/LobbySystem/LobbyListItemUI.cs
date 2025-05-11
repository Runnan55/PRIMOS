using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

public class LobbyListItemUI : MonoBehaviour
{
    private string matchId;
    public TMP_Text matchIdText;
    
    public Button joinButton;
    public TMP_Text playersCountText;
    private LobbyUIManager lobbyManager;

    public void Setup(string id, int currentPlayers, int maxPlayers, LobbyUIManager manager)
    {
        matchId = id;
        lobbyManager = manager;
        matchIdText.text = $"Sala: {id}";
        playersCountText.text = $"{currentPlayers}/{maxPlayers}";

        joinButton.interactable = currentPlayers < maxPlayers; // Desactiva si está llena

        joinButton.onClick.RemoveAllListeners(); // por si reusa el prefab
        joinButton.onClick.AddListener(JoinThisMatch);
    }

    void JoinThisMatch()
    {
        CustomRoomPlayer localPlayer = CustomRoomPlayer.LocalInstance;
        if (localPlayer != null)
        {
            localPlayer.CmdJoinMatch(matchId);
            lobbyManager.ShowRoomPanel();
            joinButton.interactable = false;
        }
    }
}
