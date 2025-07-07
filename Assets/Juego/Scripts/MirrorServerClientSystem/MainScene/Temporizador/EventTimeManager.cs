using System;
using Mirror;
using UnityEngine;

public class EventTimeManager : NetworkBehaviour

{
    public static EventTimeManager Instance { get; private set; }

    [Header("Configuración periodica")]
    public DayOfWeek[] activeDays = new DayOfWeek[] {DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday};
    public int activeHourStart = 12;
    public int activeMinuteStart = 00;
    public int activeHourEnd = 23;
    public int activeMinuteEnd = 00;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    [Server]
    public void HandleTimeRequest(NetworkConnectionToClient conn)
    {
        Debug.Log("[SERVER] Ejecutando HandleTimeRequest...");

        DateTime utcNow = DateTime.UtcNow;
        DateTime spainNow = utcNow + GetSpainOffset(utcNow);

        bool isActiveNow = IsWithinActivePeriod(spainNow, out TimeSpan timeRemaining, out DateTime nextStart);
        DateTime endToday = new DateTime(spainNow.Year, spainNow.Month, spainNow.Day, activeHourEnd, activeMinuteEnd, 0);
        DateTime eventTimeUtc = isActiveNow
            ? endToday - GetSpainOffset(endToday)
            : nextStart - GetSpainOffset(nextStart);

        Debug.Log($"[SERVER] Enviando tiempo al cliente. now={spainNow}, target={eventTimeUtc}");

        var player = conn.identity.GetComponent<CustomRoomPlayer>();
        player?.TargetReceiveTime(conn, spainNow.Ticks, eventTimeUtc.Ticks);
    }


    private bool IsWithinActivePeriod(DateTime localNow, out TimeSpan timeRemaining, out DateTime nextStart)
    {
        DayOfWeek today = localNow.DayOfWeek;
        DateTime startToday = new DateTime(localNow.Year, localNow.Month, localNow.Day, activeHourStart, activeMinuteStart, 0);
        DateTime endToday = new DateTime(localNow.Year, localNow.Month, localNow.Day, activeHourEnd, activeMinuteEnd, 0);

        if (Array.Exists(activeDays, d => d == today))
        {
            if (localNow >= startToday && localNow < endToday)
            {
                timeRemaining = endToday - localNow;
                nextStart = DateTime.MinValue;
                return true;
            }
            else if (localNow < startToday)
            {
                timeRemaining = startToday - localNow;
                nextStart = startToday;
                return false;
            }
        }

        //Buscar la próxima fecha de inicio activa
        for (int i = 1; i <= 7; i++)
        {
            DateTime next = localNow.AddDays(i);
            if (Array.Exists(activeDays, d => d == next.DayOfWeek))
            {
                nextStart = new DateTime(next.Year, next.Month, next.Day, activeHourStart, activeMinuteStart, 0);
                timeRemaining = nextStart - localNow;
                return false;
            }
        }

        //Nunca debería llegar hasta aquí
        nextStart = localNow;
        timeRemaining = TimeSpan.Zero;
        return false;
    }

    private TimeSpan GetSpainOffset(DateTime utc)
    {
        //Último domingo de marzo a último domingo de octubre -> horario de verano (UTC + 2)
        DateTime startDST = new DateTime(utc.Year, 3, 31);
        while (startDST.DayOfWeek != DayOfWeek.Sunday) startDST = startDST.AddDays(-1);

        // Último domingo de octubre
        DateTime endDST = new DateTime(utc.Year, 10, 31);
        while (endDST.DayOfWeek != DayOfWeek.Sunday) endDST = endDST.AddDays(-1);

        return (utc >= startDST && utc < endDST) ? new TimeSpan(1, 0, 0) : new TimeSpan(0, 0, 0);
    }
}
