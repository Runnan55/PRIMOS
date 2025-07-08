using System;
using Mirror;
using TMPro;
using UnityEngine;

public class ClientCountdownTimer : MonoBehaviour
{
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
        RequestTimeFromServer();
    }

    private void Update()
    {
        if (!timerStarted) return;

        DateTime estimatedNow = serverNow.AddSeconds(Time.time - timeSinceReceived);
        TimeSpan remaining = eventTime - estimatedNow;

        var lobbyUI = FindFirstObjectByType<MainLobbyUI>();
        if (lobbyUI == null) return;

        bool isEventNow = estimatedNow >= serverNow &&
                          estimatedNow.TimeOfDay >= new TimeSpan(EventTimeManager.Instance.activeHourStart, EventTimeManager.Instance.activeMinuteStart, 0) &&
                          estimatedNow.TimeOfDay < new TimeSpan(EventTimeManager.Instance.activeHourEnd, EventTimeManager.Instance.activeMinuteEnd, 0);

        if (isEventNow)
        {
            // Evento activo, mostrar tiempo restante hasta fin
            timerReachedZero = false;
            lobbyUI.OnRankedTimerFinished();
            lobbyUI.UpdateTimerUI(remaining);
        }
        else if (remaining.TotalSeconds > 0)
        {
            // Evento aún no comenzó
            timerReachedZero = false;
            lobbyUI.OnRankedTimerRemaining();
            lobbyUI.UpdateTimerUI(remaining);
        }
        else
        {
            // Evento terminó
            timerReachedZero = true;
            lobbyUI.OnRankedTimerRemaining();
        }
    }

    public void SetTimesFromServer(DateTime now, DateTime target)
    {
        Debug.Log($"[ClientCountdownTimer] SetTimesFromServer: now={now}, target={target}");

        serverNow = now;
        eventTime = target;
        timeSinceReceived = Time.time;
        timerStarted = true;
    }

    public void RequestTimeFromServer()
    {
        if (NetworkClient.isConnected && NetworkClient.connection != null)
        {
            Debug.Log("[ClientCountdownTimer] Enviando EmptyTimerMessage al servidor...");
            NetworkClient.connection.Send(new EmptyTimerMessage());
        }
        else
        {
            Debug.LogWarning("[ClientCountdownTimer] No conectado, no se envió EmptyTimerMessage.");
        }
    }
}
