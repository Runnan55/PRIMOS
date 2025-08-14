using Mirror;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using TMPro;
using Unity.VisualScripting;
using System;
using SimpleJSON;

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
    public GameObject youDontHaveTicket;

    public TMP_Text countdownText;
    public TMP_Text rankedRemainingText;

    private bool isRankedTimeAvailable = false;

    [Header("Leaderboard UI")]
    public GameObject leaderboardPanel;
    public Button leaderboardBtn;
    public Button btnBackLeaderboard;

    public Transform leaderboardContentContainer;
    public GameObject leaderboardRankedEntryPrefab;
    public GameObject localPlayerRankedEntry;

    private string _serverLeaderboardJson; // buffer para la respuesta del server

    [Header("Ticket y Llaves")]
    [SerializeField] private TMP_Text ticketText;
    [SerializeField] private TMP_Text keyText;
    private int currentTickets = 0;

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

        playButton.interactable = false;

        // Llamar a actualizar los Keys y Tickets desde Server-Firebase
        StartCoroutine(AutoRefreshWalletData());

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
                OnRankedTimeForPlay();
            else
                OnRankedTimerFinished();
        }

    }

    private IEnumerator AutoRefreshWalletData()
    {
        StartCoroutine(WaitAndRequestPlayerData());
        
        while (true)
        {
            yield return new WaitForSecondsRealtime(5f);
            CustomRoomPlayer.LocalInstance.CmdRequestTicketAndKeyStatus();
        }
    }

    private IEnumerator WaitAndRequestPlayerData()
    {
        float timeout = 5f;
        float elapsed = 0f;

        while ((CustomRoomPlayer.LocalInstance == null || !CustomRoomPlayer.LocalInstance.isLocalPlayer) && elapsed < timeout)
        {
            yield return null; // Espera un frame
            elapsed += Time.deltaTime;
        }

        if (CustomRoomPlayer.LocalInstance != null && CustomRoomPlayer.LocalInstance.isLocalPlayer)
        {
            Debug.Log("[MainLobbyUI] CustomRoomPlayer listo, solicitando nickname y wallet.");
            CustomRoomPlayer.LocalInstance.CmdRequestNicknameFromFirestore();
            CustomRoomPlayer.LocalInstance.CmdRequestTicketAndKeyStatus();
            //Ticket y llaves
            //RequestTicketStatusFromServer();
            StartCoroutine(RequestTicketStatusFromServerPeriodically());
        }
        else
        {
            Debug.LogWarning("[MainLobbyUI] No se encontró un CustomRoomPlayer válido tras 5s.");
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
                OnRankedTimeForPlay();
            else
                OnRankedTimerFinished();
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

    public void OnRankedTimeForPlay()
    {
        isRankedTimeAvailable = true;
        UpdateRankedButtonState();
    }

    public void OnRankedTimerFinished()
    {
        isRankedTimeAvailable = false;
        UpdateRankedButtonState();
    }

    private void UpdateRankedButtonState()
    {
        missingTime.SetActive(false);
        timeRemainingForPlay.SetActive(false);
        youDontHaveTicket.SetActive(false);
        rankedButton.interactable = false;

        if (currentTickets > 0)
        {
            if (isRankedTimeAvailable == true)
            {
                timeRemainingForPlay.SetActive(true);
                rankedButton.interactable = true;
            }
            else if (isRankedTimeAvailable == false)
            {
                missingTime.SetActive(true);
            }
        }
        else
        {
            youDontHaveTicket.SetActive(true);
        }

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
        //StartCoroutine(FirestoreLeaderboardFetcher.Instance.FetchAndDisplayLeaderboard());
        StartCoroutine(FetchAndDisplayLeaderboard());
    }

    private void HideLeaderboardPanel()
    {
        AudioManager.Instance.PlaySFX("Clic");
        leaderboardPanel.SetActive(false);
    }

    // Llamada desde TargetReceiveLeaderboard en el server
    public void OnServerLeaderboardJson(string json)
    {
        _serverLeaderboardJson = json;
    }

    // === LA FUNCIÓN ÚNICA QUE HACE TODO ===
    public IEnumerator FetchAndDisplayLeaderboard()
    {
        // 1) Pedir al server
        _serverLeaderboardJson = null;
        CustomRoomPlayer.LocalInstance?.CmdFetchLeaderboard();

        // 2) Esperar respuesta (timeout de cortesía)
        float t = 0f, timeout = 10f;
        while (_serverLeaderboardJson == null && t < timeout)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (_serverLeaderboardJson == null)
        {
            Debug.LogWarning("[Leaderboard] Timeout esperando datos del server.");
            yield break;
        }

        // 3) Parsear JSON y pintar UI
        var arr = JSON.Parse(_serverLeaderboardJson)?.AsArray;
        var users = new List<(string name, int points)>();
        if (arr != null)
        {
            foreach (var n in arr)
            {
                var v = n.Value;
                users.Add((v["name"], v["points"].AsInt));
            }
        }

        foreach (Transform child in leaderboardContentContainer)
            Destroy(child.gameObject);

        for (int i = 0; i < users.Count; i++)
        {
            var go = Instantiate(leaderboardRankedEntryPrefab, leaderboardContentContainer);
            var ui = go.GetComponent<LeaderboardEntryUI>();
            if (ui != null) ui.SetEntry(i + 1, users[i].name, users[i].points);
        }

        // Tu fila (como antes: solo si entras en el Top-100; si no, “No Rank”)
        if (localPlayerRankedEntry != null)
        {
            string localName = nameInputField?.text?.Trim();
            var ui = localPlayerRankedEntry.GetComponent<LeaderboardEntryUI>();
            if (ui != null)
            {
                int idx = users.FindIndex(u => u.name.Trim()
                    .Equals(localName, StringComparison.OrdinalIgnoreCase));
                if (idx != -1)
                {
                    localPlayerRankedEntry.SetActive(true);
                    ui.SetEntry(idx + 1, users[idx].name, users[idx].points);
                }
                else
                {
                    localPlayerRankedEntry.SetActive(true);
                    ui.SetEntry(-1, string.IsNullOrEmpty(localName) ? "You" : localName + " (No Rank)", 0);
                }
            }
        }
    }

    #endregion

    #region Ticket

    public void UpdateTicketAndKeyDisplay(int tickets, int keys)
    {
        currentTickets = tickets;

        ticketText.text = tickets.ToString();
        keyText.text = keys.ToString();

        UpdateRankedButtonState();
    }

    private IEnumerator RequestTicketStatusFromServerPeriodically()
    {
        CustomRoomPlayer.LocalInstance?.CmdRequestTicketAndKeyStatus();

        while (true)
        {
            yield return new WaitForSecondsRealtime(5f);
        }
    }

    #endregion
}
