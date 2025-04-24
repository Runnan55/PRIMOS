using Mirror;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class MainLobbyUI : MonoBehaviour
{
    public Button rankedButton;
    public Button casualButton;
    public Button exitButton;

    private Dictionary<string, string> modeToScene = new Dictionary<string, string>()
    {
        { "Casual", "LobbySceneCasual" },
        { "Ranked", "LobbySceneRanked" },
        // Podés agregar más modos fácilmente:
        // { "Torneo", "LobbySceneTorneo" },
        // { "Evento", "LobbySceneEvento" }
    };

    private void Start()
    {
        rankedButton.onClick.AddListener(() => JoinMode("Ranked"));
        casualButton.onClick.AddListener(() => JoinMode("Casual"));
        exitButton.onClick.AddListener(() => Application.Quit());
    }

    private void JoinMode(string mode)
    {
        // Verificamos que el modo exista en el diccionario
        if (!modeToScene.TryGetValue(mode, out string sceneName))
        {
            Debug.LogError($"[MainLobbyUI] Modo '{mode}' no tiene una escena asignada.");
            return;
        }

        // 1. Avisar al servidor en qué modo queremos entrar
        CustomRoomPlayer.LocalInstance?.CmdRequestJoinLobbyScene(mode);

        // 2. Cambiar escena en el cliente
        SceneLoaderManager.Instance.LoadScene(sceneName);
    }
}
