using Mirror;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
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

    [Header("Opcional")]
    public NicknameUI nicknameUI;
    public FirestoreUserManager userManager;

    [Header("Panel Opciones y Audio")]
    public GameObject settingsPanel;
    public GameObject audioPanel;

    public Button settingsButton;
    public Button audioButton;
    public Button backFromSettingsButton;
    public Button backFromAudioButton;

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
        AudioManager.Instance.PlayMusic("MenuTheme");

        nameInputField.characterLimit = 11;

        nameInputField.onEndEdit.AddListener(OnNameEntered);
        playButton.onClick.AddListener(() => StartGameSelectionMenu());
        backToStartMenuButton.onClick.AddListener(() => BackToStartMenu());

        settingsButton.onClick.AddListener(OpenSettingsPanel);
        audioButton.onClick.AddListener(OpenAudioPanel);
        backFromSettingsButton.onClick.AddListener(CloseSettingsPanel);
        backFromAudioButton.onClick.AddListener(CloseAudioPanel);

        playButton.interactable = false;

        if (userManager != null)
        {
            userManager.LoadUser(); // Esto carga el nickname de Firebase

            StartCoroutine(OnNameEnteredDelayed()); // Esto actualiza el playerName en el server de Mirror, sino aparece en blanco hasta que actualizemos
        }

        nameInputField.onValueChanged.AddListener(OnNameChangedLive);

        //De momento rankedButton no hace nada
        //rankedButton.onClick.AddListener(() => JoinMode("Ranked")); 
        comingSoonButton.onClick.AddListener(() => Debug.Log("Este modo aún no está disponible."));
        casualButton.onClick.AddListener(() => JoinMode("Casual"));
        //exitButton.onClick.AddListener(() => Application.Quit());
        //Desactivé el exit button por mientras pq bugea en la web
    }

    private IEnumerator OnNameEnteredDelayed()
    {
        yield return new WaitForSeconds(0.2f);

        string name = nameInputField.text.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            OnNameEntered(name);
        }
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
            return;
        }

        playButton.interactable = true;

        nicknameUI.nicknameInput.text = newText;
        nicknameUI.SaveNickname();
    }

    #region Opciones Y Audio

    private void OpenSettingsPanel()
    {
        AudioManager.Instance.PlaySFX("Clic");

        settingsPanel.SetActive(true);
        audioPanel.SetActive(false); // por si estaba abierto
    }

    private void CloseSettingsPanel()
    {
        settingsPanel.SetActive(false);
    }

    private void OpenAudioPanel()
    {
        audioPanel.SetActive(true);
    }

    private void CloseAudioPanel()
    {
        audioPanel.SetActive(false);
    }


    #endregion

    public void StartGameSelectionMenu()
    {
        AudioManager.Instance.PlaySFX("Clic");

        gameSelectionCanvas.SetActive(true);
        startMenuCanvas.SetActive(false);
    }

    private void BackToStartMenu()
    {
        AudioManager.Instance.PlaySFX("Clic");

        startMenuCanvas.SetActive(true);
        gameSelectionCanvas.SetActive(false);
    }

    private void JoinMode(string mode)
    {
        AudioManager.Instance.PlaySFX("Clic");

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

    //Esto usa referencia al AuthManager y Firebase, ojo con los cruces
    public void OnLogoutButton()
    {
        AudioManager.Instance.PlaySFX("Clic");

        if (NetworkClient.isConnected)
        {
            NetworkManager.singleton.StopClient();
        }

        AuthManager.Instance.Logout();
    }
}
