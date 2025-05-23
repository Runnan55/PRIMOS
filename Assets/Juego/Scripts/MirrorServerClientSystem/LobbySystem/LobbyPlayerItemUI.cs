using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LobbyPlayerItemUI : MonoBehaviour
{
    public TMP_Text playerNameText;
    //public TMP_Text readyStatusText;
    public Button kickButton;
    public GameObject adminIcon;
    public GameObject readyIcon;

    private string playerId;
    private LobbyUIManager lobbyManager;

    public void Setup(string name, bool isReady, bool showKickButton, LobbyUIManager manager, string playerId, bool isAdmin)
    {
        this.playerId = playerId;
        lobbyManager = manager;

        playerNameText.text = name;

        // Nuevo: activar imagen de Ready
        if (readyIcon != null)
        {
            readyIcon.SetActive(isReady);
        }

        kickButton.gameObject.SetActive(showKickButton);
        adminIcon.gameObject.SetActive(isAdmin);

        kickButton.onClick.RemoveAllListeners();
        kickButton.onClick.AddListener(KickThisPlayer);
    }

    private void KickThisPlayer()
    {
        lobbyManager.KickPlayer(playerId);
    }
}
