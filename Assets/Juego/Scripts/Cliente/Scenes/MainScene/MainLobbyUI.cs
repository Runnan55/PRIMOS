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
    //public GameObject nicknameUI;
    //public FirestoreUserManager userManager;

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

    [Header("LoadingScreen")]
    [SerializeField] private GameObject loadingScreen;

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
        nameInputField.onValueChanged.AddListener(OnNameChangedLive);

        playButton.onClick.AddListener(() => StartGameSelectionMenu());
        backToStartMenuButton.onClick.AddListener(() => BackToStartMenu());

        settingsButton.onClick.AddListener(OpenSettingsPanel);
        audioButton.onClick.AddListener(OpenAudioPanel);
        backFromSettingsButton.onClick.AddListener(CloseSettingsPanel);
        backFromAudioButton.onClick.AddListener(CloseAudioPanel);

        CursorSetup.I?.UsePinkCursor();

        SetupLeaderboardButtons();

        playButton.interactable = false;

        // Llamar a actualizar los Keys y Tickets desde Server-Firebase
        StartCoroutine(AutoRefreshWalletData());

        //Llamar a ocultar la pantalla de carga cuando el server nos de el OK
        StartCoroutine(HideLoadingWhenLoginAccepted());

        rankedButton.onClick.AddListener(() => JoinMode("Ranked")); 
        comingSoonButton.onClick.AddListener(() => LogWithTime.Log("Este modo aún no está disponible."));
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

    private IEnumerator HideLoadingWhenLoginAccepted()
    {
        // Asegúrate de que el overlay esté encendido desde el primer frame
        if (loadingScreen) loadingScreen.SetActive(true);

        // Espera a que el server cree al jugador local (señal de login OK)
        while (NetworkClient.isConnected &&
               (CustomRoomPlayer.LocalInstance == null || !CustomRoomPlayer.LocalInstance.isLocalPlayer))
        {
            yield return null;
        }

        // Si nos desconectaron (duplicado/kick), no quites el overlay
        if (!NetworkClient.isConnected) yield break;

        // OK: ahora sí quitamos el overlay y puede verse el menú
        if (loadingScreen) loadingScreen.SetActive(false);
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
            LogWithTime.Log("[MainLobbyUI] CustomRoomPlayer listo, solicitando nickname y wallet.");
            CustomRoomPlayer.LocalInstance.CmdRequestNicknameFromFirestore();
            CustomRoomPlayer.LocalInstance.CmdRequestTicketAndKeyStatus();
            //Ticket y llaves
            //RequestTicketStatusFromServer();
            StartCoroutine(RequestTicketStatusFromServerPeriodically());
        }
        else
        {
            LogWithTime.LogWarning("[MainLobbyUI] No se encontró un CustomRoomPlayer válido tras 5s.");
        }
    }

    private void OnNameEntered(string playerName)
    {
        string enteredName = nameInputField.text.Trim();

        if (CustomRoomPlayer.LocalInstance != null)
            CustomRoomPlayer.LocalInstance.CmdUpdateNickname(enteredName);

        // Activar el botón solo si hay nombre
        playButton.interactable = true;

        LogWithTime.Log("Nombre confirmado: " + enteredName);
    }

    private void OnNameChangedLive(string playerName)
    {
        string enteredName = nameInputField.text.Trim();

        if (string.IsNullOrEmpty(enteredName))
        {
            LogWithTime.LogWarning("El nombre no puede estar vacío");
            nameInputField.text = "";
            nameInputField.placeholder.GetComponent<TMP_Text>().text = "Enter name first!";
            playButton.interactable = false;
        }
        else
        {
            playButton.interactable = true;
        }
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
            LogWithTime.LogError($"[MainLobbyUI] Modo '{mode}' no tiene una escena asignada.");
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

    public void OnRankedAlwaysAvailableNoCountdown()
    {
        isRankedTimeAvailable = true;

        missingTime.SetActive(false);
        timeRemainingForPlay.SetActive(false); // Ocultar panel de tiempo
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
            LogWithTime.LogWarning("[Leaderboard] Timeout esperando datos del server.");
            yield break;
        }

        // 3) Parsear JSON (nuevo: {top,self} | viejo: array plano)
        var root = JSON.Parse(_serverLeaderboardJson);
        var users = new List<(string name, int points)>();
        JSONArray top = null;
        JSONObject self = null;

        if (root != null && root.IsObject)
        {
            top = root["top"]?.AsArray;
            self = root["self"]?.AsObject;
        }
        if (top == null && root != null && root.IsArray)
        {
            top = root.AsArray;
        }

        if (top != null)
        {
            foreach (var n in top)
            {
                var v = n.Value;
                string nName = v["name"];
                int pts = v["points"]?.AsInt ?? 0;
                users.Add((nName, pts));
            }
        }

        // 4) Pintar lista Top-N
        foreach (Transform child in leaderboardContentContainer)
            Destroy(child.gameObject);

        for (int i = 0; i < users.Count; i++)
        {
            var go = Instantiate(leaderboardRankedEntryPrefab, leaderboardContentContainer);
            var ui = go.GetComponent<LeaderboardEntryUI>();
            if (ui != null) ui.SetEntry(i + 1, users[i].name, users[i].points);
        }

        // 5) Fila local (usa "self" por UID; si no viene, fallback por nombre/jugador local)
        if (localPlayerRankedEntry != null)
        {
            var ui = localPlayerRankedEntry.GetComponent<LeaderboardEntryUI>();
            if (ui != null)
            {
                if (self != null)
                {
                    int selfRank = self["rank"]?.AsInt ?? -1;

                    // corregido: si no hay "name" en JSON, usar playerName local
                    string selfName = self["name"];
                    if (string.IsNullOrEmpty(selfName))
                    {
                        selfName = CustomRoomPlayer.LocalInstance != null
                            ? CustomRoomPlayer.LocalInstance.playerName
                            : "You";
                    }

                    int selfPoints = self["points"]?.AsInt ?? 0;

                    localPlayerRankedEntry.SetActive(true);
                    if (selfRank > 0) ui.SetEntry(selfRank, selfName, selfPoints);
                    else ui.SetEntry(0, selfName + " (No Rank)", selfPoints);
                }
                else
                {
                    // Legacy: buscar por nombre del InputField
                    string localName = nameInputField?.text?.Trim();
                    int idx = users.FindIndex(u => u.name.Trim()
                        .Equals(localName, StringComparison.OrdinalIgnoreCase));

                    localPlayerRankedEntry.SetActive(true);
                    if (idx != -1) ui.SetEntry(idx + 1, users[idx].name, users[idx].points);
                    else
                    {
                        // fallback: usar playerName local si existe
                        string fallbackName = !string.IsNullOrEmpty(localName)
                            ? localName
                            : (CustomRoomPlayer.LocalInstance != null
                                ? CustomRoomPlayer.LocalInstance.playerName
                                : "You");
                        ui.SetEntry(-1, fallbackName + " (No Rank)", 0);
                    }
                }
            }
        }

        // 6) Forzar layout limpio y reset de scroll/offset
        Canvas.ForceUpdateCanvases();

        var rt = leaderboardContentContainer as RectTransform;
        if (rt != null)
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            rt.anchoredPosition = Vector2.zero;
        }

        var scroll = leaderboardPanel != null
            ? leaderboardPanel.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true)
            : null;
        if (scroll != null)
        {
            scroll.normalizedPosition = new Vector2(0f, 1f);
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
