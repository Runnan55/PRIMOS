using System;
using Mirror;
using UnityEngine;

public class EventTimeManager : NetworkBehaviour

{
    public static EventTimeManager Instance { get; private set; }

    [Header("Hora objetivo (hora de España)")]
    [SerializeField] private int targetYear = 2025;
    [SerializeField] private int targetMonth = 7;
    [SerializeField] private int targetDay = 5;
    [SerializeField] private int targetHour = 20;
    [SerializeField] private int targetMinute = 00;

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
        DateTime utcNow = DateTime.UtcNow;
        DateTime spainNow = utcNow + GetSpainOffset(utcNow);

        DateTime localTarget = new DateTime(targetYear, targetMonth, targetDay, targetHour, targetMinute, 0);
        DateTime targetUtc = localTarget - GetSpainOffset(localTarget);

        TargetReceiveTime(conn, spainNow.Ticks, targetUtc.Ticks);
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

    [TargetRpc]
    private void TargetReceiveTime(NetworkConnectionToClient target, long nowTicks, long targetTicks)
    {
        ClientCountdownTimer.Instance?.SetTimesFromServer(new DateTime(nowTicks), new DateTime(targetTicks));
    }
}
