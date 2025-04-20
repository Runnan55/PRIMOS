using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LobbyPlayerItemUI : MonoBehaviour
{
    public TMP_Text playerNameText;
    public TMP_Text readyStatusText;
    public Button kickButton;

    private string playerId;
    private LobbyUIManager lobbyManager;

    public void Setup(string name, bool isReady, bool showKickButton, LobbyUIManager manager, string playerId)
    {
        this.playerId = playerId;
        lobbyManager = manager;

        playerNameText.text = name;
        readyStatusText.text = isReady ? "Ready" : "Not Ready";
        readyStatusText.color = isReady ? Color.green : Color.red;

        kickButton.gameObject.SetActive(showKickButton);
        kickButton.onClick.AddListener(KickThisPlayer);
    }

    private void KickThisPlayer()
    {
        lobbyManager.KickPlayer(playerId);
    }
}
