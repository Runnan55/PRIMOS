using System;
using Mirror;
using TMPro;
using UnityEngine;

public class ClientCountdownTimer : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text countdownText;
    public TMP_Text rankedRemainingText;

    private DateTime serverNow;
    private DateTime eventTime;
    private float timeSinceReceived;
    private bool timerStarted;
    public bool timerReachedZero = false;

    public static ClientCountdownTimer Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        InvokeRepeating(nameof(RequestTimeFromServer), 1f, 30f); //Actualiza cada 30s
    }

    private void Update()
    {
        if (!timerStarted) return;

        DateTime estimatedNow = serverNow.AddSeconds(Time.time - timeSinceReceived);
        TimeSpan remaining = eventTime - estimatedNow;

        if (remaining.TotalSeconds <= 0)
        {
            countdownText.text = "¡El evento ha comenzado!";

            timerReachedZero = true;
            var lobbyUI = FindFirstObjectByType<MainLobbyUI>();
            if (lobbyUI != null)
                lobbyUI.OnRankedTimerFinished();

            // Calcular cuánto queda de tiempo activo
            TimeSpan tiempoActivoRestante = eventTime - estimatedNow;
            if (rankedRemainingText != null)
            {
                if (tiempoActivoRestante.TotalSeconds > 0)
                {
                    rankedRemainingText.text = $"Ranked disponible por {tiempoActivoRestante.Hours:D2}:{tiempoActivoRestante.Minutes:D2}:{tiempoActivoRestante.Seconds:D2}";
                    rankedRemainingText.gameObject.SetActive(true);
                }
                else
                {
                    rankedRemainingText.text = "";
                    rankedRemainingText.gameObject.SetActive(false);
                }
            }
        }

        else
        {
            timerReachedZero = false;
            var lobbyUI = FindFirstObjectByType<MainLobbyUI>();
            if (lobbyUI != null)
                lobbyUI.OnRankedTimeRemaining();

            countdownText.text = $"{remaining.Days}d {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"; //Tiempo faltante
        }
    }

    public void SetTimesFromServer(DateTime now, DateTime target)
    {
        serverNow = now;
        eventTime = target;
        timeSinceReceived = Time.time;
        timerStarted = true;
    }

    private void RequestTimeFromServer()
    {
        if (NetworkClient.isConnected && NetworkClient.connection != null)
        {
            NetworkClient.connection.Send(new EmptyTimerMessage());
        }
    }
}
