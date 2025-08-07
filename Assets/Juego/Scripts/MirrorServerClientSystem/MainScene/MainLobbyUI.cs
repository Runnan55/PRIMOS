using Mirror;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using TMPro;
using Unity.VisualScripting;
using System;

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

    [Header("Timer para Ranked")]
    public GameObject missingTime;
    public GameObject timeRemainingForPlay;

    public TMP_Text countdownText;
    public TMP_Text rankedRemainingText;

    [Header("Leaderboard UI")]
    public GameObject leaderboardPanel;
    public Button leaderboardBtn;
    public Button btnBackLeaderboard;

    [Header("Ticket y Llaves")]
    [SerializeField] private TMP_Text ticketText;
    [SerializeField] private TMP_Text keyText;

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

        nameInputField.characterLimit = 14;

        nameInputField.onEndEdit.AddListener(OnNameEntered);
        playButton.onClick.AddListener(() => StartGameSelectionMenu());
        backToStartMenuButton.onClick.AddListener(() => BackToStartMenu());

        settingsButton.onClick.AddListener(OpenSettingsPanel);
        audioButton.onClick.AddListener(OpenAudioPanel);
        backFromSettingsButton.onClick.AddListener(CloseSettingsPanel);
        backFromAudioButton.onClick.AddListener(CloseAudioPanel);

        SetupLeaderboardButtons();

        //Ticket y llaves
        //RequestTicketStatusFromServer();
        StartCoroutine(RequestTicketStatusFromServerPeriodically());

        playButton.interactable = false;

        /*if (userManager != null)
        {
            userManager.LoadUser(); // Esto carga el nickname de Firebase

            StartCoroutine(OnNameEnteredDelayed()); // Esto actualiza el playerName en el server de Mirror, sino aparece en blanco hasta que actualizemos
        }*/

        if (CustomRoomPlayer.LocalInstance != null)
        {
            CustomRoomPlayer.LocalInstance.CmdRequestNicknameFromFirestore();
        }

        nameInputField.onValueChanged.AddListener(OnNameChangedLive);

        rankedButton.onClick.AddListener(() => JoinMode("Ranked")); 
        comingSoonButton.onClick.AddListener(() => Debug.Log("Este modo aún no está disponible."));
        casualButton.onClick.AddListener(() => JoinMode("Casual"));
        //exitButton.onClick.AddListener(() => Application.Quit());
        //Desactivé el exit button por mientras pq bugea en la web

        var timer = FindFirstObjectByType<ClientCountdownTimer>();
        if (timer != null)
        {
            if (timer.timerReachedZero)
                OnRankedTimerFinished();
            else
                OnRankedTimerRemaining();
        }

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

        CustomRoomPlayer.LocalInstance?.CmdRequestTicketAndKeyStatus();

        //Parte del sincronizador de Timer de Ranked
        //Asegura el estado correcto del botón Ranked apenas se entra
        var countdown = FindFirstObjectByType<ClientCountdownTimer>();
        if (countdown != null)
        {
            countdown.RequestTimeFromServer();

            if (countdown.timerReachedZero == true)
                OnRankedTimerFinished();
            else
                OnRankedTimerRemaining();
        }
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

    #region Timer para Ranked

    public void OnRankedTimerFinished()
    {
        if (rankedButton != null)
            rankedButton.interactable = true;

        if (missingTime != null)
            missingTime.SetActive(false);

        if (timeRemainingForPlay != null)
            timeRemainingForPlay.SetActive(true);
    }

    public void OnRankedTimerRemaining()
    {
        if (rankedButton != null)
            rankedButton.interactable = false;

        if (missingTime != null)
            missingTime.SetActive(true);

        if (timeRemainingForPlay != null)
            timeRemainingForPlay.SetActive(false);
    }

    public void UpdateCountdownToEvent(TimeSpan remaining)
    {
        if (countdownText != null)
            countdownText.text = $"{remaining.Days}d {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    public void UpdateRankedRemainingTime(TimeSpan remaining)
    {
        if (rankedRemainingText != null)
            rankedRemainingText.text = $"{remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    #endregion

    #region LeaderboardUI

    private void SetupLeaderboardButtons()
    {
        leaderboardBtn.onClick.AddListener(() => ShowLeaderboardPanel());
        btnBackLeaderboard.onClick.AddListener(() => HideLeaderboardPanel());
    }

    private void ShowLeaderboardPanel()
    {
        AudioManager.Instance.PlaySFX("Clic");
        leaderboardPanel.SetActive(true);
        StartCoroutine(FirestoreLeaderboardFetcher.Instance.FetchAndDisplayLeaderboard());
    }

    private void HideLeaderboardPanel()
    {
        AudioManager.Instance.PlaySFX("Clic");
        leaderboardPanel.SetActive(false);
    }

    #endregion

    #region Ticket

    public void UpdateTicketAndKeyDisplay(int tickets, int keys)
    {
        ticketText.text = tickets.ToString();
        keyText.text = keys.ToString();

        rankedButton.interactable = tickets > 0;
    }

    /*public void RequestTicketStatusFromServer()
    {
        CustomRoomPlayer.LocalInstance?.CmdRequestTicketAndKeyStatus();
    }*/

    private IEnumerator RequestTicketStatusFromServerPeriodically()
    {
        CustomRoomPlayer.LocalInstance?.CmdRequestTicketAndKeyStatus();

        while (true)
        {
            yield return new WaitForSeconds(5f);
        }
    }

    #endregion
}
