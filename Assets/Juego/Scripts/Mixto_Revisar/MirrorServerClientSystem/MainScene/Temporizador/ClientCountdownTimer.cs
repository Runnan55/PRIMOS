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

    void Awake()
    {
#if UNITY_SERVER
    enabled = false; // o Destroy(this);
    return;
#endif
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

        if (isActivePeriod)
        {
            if (remaining.TotalSeconds > 0)
            {
                timerReachedZero = false;
                lobbyUI.OnRankedTimeForPlay();
                lobbyUI.UpdateRankedRemainingTime(remaining); // <<< NUEVO
            }
            else
            {
                timerReachedZero = true;
                lobbyUI.OnRankedTimerFinished(); // evento terminó
            }
        }
        else
        {
            if (remaining.TotalSeconds > 0)
            {
                timerReachedZero = false;
                lobbyUI.OnRankedTimerFinished();
                lobbyUI.UpdateCountdownToEvent(remaining); // <<< NUEVO
            }
            else
            {
                timerReachedZero = true;
                lobbyUI.OnRankedTimeForPlay(); // evento acaba de comenzar
            }
        }
    }

    private bool isActivePeriod = false;

    public void SetTimesFromServer(DateTime now, DateTime target, bool isActive)
    {
        serverNow = now;
        eventTime = target;
        timeSinceReceived = Time.time;
        timerStarted = true;
        isActivePeriod = isActive;
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
            Debug.LogWarning("[ClientCountdownTimer] No conectado, no se envio EmptyTimerMessage.");
        }
    }
}
