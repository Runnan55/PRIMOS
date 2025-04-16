using Mirror;
using UnityEngine;
using UnityEngine.UI;

public class MainLobbyUI : MonoBehaviour
{
    public Button rankedButton;
    public Button casualButton;
    public Button exitButton;

    private void Start()
    {
        rankedButton.onClick.AddListener(() => StartGame("RankedScene"));
        casualButton.onClick.AddListener(() => StartGame("CasualScene"));
        exitButton.onClick.AddListener(() => Application.Quit());
    }

    void StartGame(string sceneName)
    {
        if (NetworkServer.active && NetworkClient.isConnected)
        {
            CustomNetworkManager custom = (CustomNetworkManager)NetworkManager.singleton;
            custom.SendPlayerToModeScene(NetworkServer.localConnection, sceneName);
        }
        else if (NetworkClient.isConnected)
        {
            Debug.Log("Soy un cliente putin, necesito enviar Command al host para cambiar de escena");
        }
    }
}
