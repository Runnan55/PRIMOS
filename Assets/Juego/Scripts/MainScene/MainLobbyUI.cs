using Mirror;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class MainLobbyUI : MonoBehaviour
{
    [Header("Canvas")]
    public GameObject startMenuCanvas;
    public GameObject gameSelectionCanvas;

    [Header("StartMenuButtons")]
    public Button playButton;
    public Button exitButton;
    public TMP_InputField nameInputField;

    [Header("GameSelectionButtons")]
    public Button rankedButton;
    public Button casualButton;
    public Button comingSoonButton;
    public Button backToStartMenuButton;

    private Dictionary<string, string> modeToScene = new Dictionary<string, string>()
    {
        { "Casual", "LobbySceneCasual" },
        { "Ranked", "LobbySceneRanked" },
        // Se agregarán más modos fácilmente:
        // { "Torneo", "LobbySceneTorneo" },
        // { "Evento", "LobbySceneEvento" }
    };

    private void Start()
    {
        nameInputField.characterLimit = 11;

        nameInputField.onEndEdit.AddListener(OnNameEntered);
        playButton.onClick.AddListener(() => StartGameSelectionMenu());
        backToStartMenuButton.onClick.AddListener(() => BackToStartMenu());

        playButton.interactable = false;

        // Cargar nombre si ya hay uno guardado
        if (GameDataManager.Instance.HasData)
        {
            string savedName = GameDataManager.Instance.CurrentData.playerName;
            if (!string.IsNullOrEmpty(savedName))
            {
                nameInputField.text = savedName;
                playButton.interactable = true;
            }
        }

        nameInputField.onValueChanged.AddListener(OnNameChangedLive);

        //De momento rankedButton no hace nada
        //rankedButton.onClick.AddListener(() => JoinMode("Ranked")); 
        comingSoonButton.onClick.AddListener(() => Debug.Log("Este modo aún no está disponible."));
        casualButton.onClick.AddListener(() => JoinMode("Casual"));
        //exitButton.onClick.AddListener(() => Application.Quit());
        //Desactivé el exit button por mientras pq bugea en la web
    }

    private void OnNameEntered(string playerName)
    {
        string enteredName = nameInputField.text.Trim();

        if (string.IsNullOrEmpty(enteredName))
        {
            Debug.LogWarning("El nombre no puede estar vacío");
            nameInputField.text = "";
            nameInputField.placeholder.GetComponent<TMP_Text>().text = "Enter name first!";
            playButton.interactable = false;
            return;
        }

        // Activar el botón solo si hay nombre
        playButton.interactable = true;

        // Enviar al servidor
        if (NetworkClient.isConnected && NetworkClient.connection != null)
        {
            NetworkClient.connection.Send(new NameMessage { playerName = enteredName });
        }
        else
        {
            Debug.LogWarning("[MainLobbyUI] No se puede enviar el nombre: no hay conexión.");
        }

        // Cargar datos del jugador
       // GameDataManager.Instance.LoadOrCreateData(AuthManager.LocalUserId, enteredName);

        Debug.Log("Nombre confirmado: " + enteredName);
    }

    private void OnNameChangedLive(string newText)
    {
        // Activar o desactivar el botón Play dinámicamente
        playButton.interactable = !string.IsNullOrWhiteSpace(newText);

        // También podés resetear el placeholder si estaba modificado
        if (string.IsNullOrWhiteSpace(newText))
        {
            nameInputField.placeholder.GetComponent<TMP_Text>().text = "Enter name";
        }
    }

    public void StartGameSelectionMenu()
    {
        gameSelectionCanvas.SetActive(true);
        startMenuCanvas.SetActive(false);
    }

    private void BackToStartMenu()
    {
        startMenuCanvas.SetActive(true);
        gameSelectionCanvas.SetActive(false);
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
