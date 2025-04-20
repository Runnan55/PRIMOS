using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainLobbyUI : MonoBehaviour
{
    public Button rankedButton;
    public Button casualButton;
    public Button exitButton;

    private void Start()
    {
        rankedButton.onClick.AddListener(() => StartGame("LobbySceneRanked"));
        casualButton.onClick.AddListener(() => StartGame("LobbySceneCasual"));
        exitButton.onClick.AddListener(() => Application.Quit());
    }

    void StartGame(string mode)
    {
        string lobbySceneName = mode == "Ranked" ? "lobbySceneRanked" : "LobbySceneCasual";
        SceneLoaderManager.Instance.LoadSceneAdditive(lobbySceneName);
    }
}
