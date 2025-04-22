using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

public class LobbyListItemUI : MonoBehaviour
{
    public TMP_Text matchIdText;
    public Button joinButton;

    private string matchId;
    private LobbyUIManager lobbyManager;

    public void Setup(string id, LobbyUIManager manager)
    {
        matchId = id;
        lobbyManager = manager;
        matchIdText.text = $"Sala: {id}";
        joinButton.onClick.AddListener(JoinThisMatch);
    }

    void JoinThisMatch()
    {
        CustomRoomPlayer localPlayer = NetworkClient.connection.identity.GetComponent<CustomRoomPlayer>();
        if (localPlayer != null)
        {
            localPlayer.CmdJoinMatch(matchId);

            // Mostrar el RoomPanel cuando te unes
            FindFirstObjectByType<LobbyUIManager>()?.ShowRoomPanel();
        }
    }
}
