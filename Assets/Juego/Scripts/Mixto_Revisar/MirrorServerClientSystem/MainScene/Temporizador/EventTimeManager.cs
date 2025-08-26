using System;
using Mirror;
using UnityEngine;

public class EventTimeManager : NetworkBehaviour
{
    public static EventTimeManager Instance { get; private set; }

    // MONDAY
    [Header("Lunes")]
    public bool lunesEnabled = true;
    [Range(0, 23)] public int lunesStartHour = 12;
    [Range(0, 59)] public int lunesStartMinute = 0;
    [Range(0, 23)] public int lunesEndHour = 23;
    [Range(0, 59)] public int lunesEndMinute = 0;

    // TUESDAY
    [Header("Martes")]
    public bool martesEnabled = true;
    [Range(0, 23)] public int martesStartHour = 12;
    [Range(0, 59)] public int martesStartMinute = 0;
    [Range(0, 23)] public int martesEndHour = 23;
    [Range(0, 59)] public int martesEndMinute = 0;

    // WEDNESDAY
    [Header("Miercoles")]
    public bool miercolesEnabled = true;
    [Range(0, 23)] public int miercolesStartHour = 12;
    [Range(0, 59)] public int miercolesStartMinute = 0;
    [Range(0, 23)] public int miercolesEndHour = 23;
    [Range(0, 59)] public int miercolesEndMinute = 0;

    // THURSDAY
    [Header("Jueves")]
    public bool juevesEnabled = true;
    [Range(0, 23)] public int juevesStartHour = 12;
    [Range(0, 59)] public int juevesStartMinute = 0;
    [Range(0, 23)] public int juevesEndHour = 23;
    [Range(0, 59)] public int juevesEndMinute = 0;

    // FRIDAY
    [Header("Viernes")]
    public bool viernesEnabled = true;
    [Range(0, 23)] public int viernesStartHour = 12;
    [Range(0, 59)] public int viernesStartMinute = 0;
    [Range(0, 23)] public int viernesEndHour = 23;
    [Range(0, 59)] public int viernesEndMinute = 0;

    // SATURDAY
    [Header("Sabado")]
    public bool sabadoEnabled = true;
    [Range(0, 23)] public int sabadoStartHour = 12;
    [Range(0, 59)] public int sabadoStartMinute = 0;
    [Range(0, 23)] public int sabadoEndHour = 23;
    [Range(0, 59)] public int sabadoEndMinute = 0;

    // SUNDAY
    [Header("Domingo")]
    public bool domingoEnabled = true;
    [Range(0, 23)] public int domingoStartHour = 12;
    [Range(0, 59)] public int domingoStartMinute = 0;
    [Range(0, 23)] public int domingoEndHour = 23;
    [Range(0, 59)] public int domingoEndMinute = 0;

    [Header("Debug/Override")]
    [SerializeField] private bool forceRankedAlwaysActive = false;

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

        bool isActive;
        DateTime target;

        if (forceRankedAlwaysActive)
        {
            isActive = true;
            target = spainNow.AddYears(10); // un valor arbitrario lejano
        }
        else
        {
            isActive = IsWithinActivePeriod(spainNow, out DateTime nextStart, out DateTime todayEnd);
            target = isActive ? todayEnd : nextStart;
        }

        var player = (conn != null && conn.identity != null)
            ? conn.identity.GetComponent<CustomRoomPlayer>() : null;

        player?.TargetReceiveTime(conn, spainNow.Ticks, target.Ticks, isActive);
    }

    private bool IsWithinActivePeriod(DateTime localNow, out DateTime nextStart, out DateTime todayEnd)
    {
        // Today
        if (TryGetWindowForDate(localNow, out DateTime start, out DateTime end))
        {
            if (localNow >= start && localNow < end)
            {
                todayEnd = end;
                nextStart = DateTime.MinValue;
                return true;
            }
            if (localNow < start)
            {
                todayEnd = end;
                nextStart = start;
                return false;
            }
        }

        // Next valid day (max 7 days ahead)
        for (int d = 1; d <= 7; d++)
        {
            DateTime nd = localNow.AddDays(d);
            if (TryGetWindowForDate(nd, out DateTime nStart, out _))
            {
                nextStart = nStart;
                todayEnd = DateTime.MinValue;
                return false;
            }
        }

        // Fallback
        nextStart = localNow;
        todayEnd = DateTime.MinValue;
        return false;
    }

    private bool TryGetWindowForDate(DateTime date, out DateTime start, out DateTime end)
    {
        GetDayWindow(date.DayOfWeek, out bool enabled, out int sh, out int sm, out int eh, out int em);

        start = new DateTime(date.Year, date.Month, date.Day,
                             Mathf.Clamp(sh, 0, 23),
                             Mathf.Clamp(sm, 0, 59), 0);
        end = new DateTime(date.Year, date.Month, date.Day,
                             Mathf.Clamp(eh, 0, 23),
                             Mathf.Clamp(em, 0, 59), 0);

        bool valid = enabled && end > start; // no midnight crossing in this version
        if (!valid) { start = default; end = default; }
        return valid;
    }

    private void GetDayWindow(DayOfWeek dow, out bool enabled, out int sh, out int sm, out int eh, out int em)
    {
        switch (dow)
        {
            case DayOfWeek.Sunday:
                enabled = domingoEnabled; sh = domingoStartHour; sm = domingoStartMinute; eh = domingoEndHour; em = domingoEndMinute; break;
            case DayOfWeek.Monday:
                enabled = lunesEnabled; sh = lunesStartHour; sm = lunesStartMinute; eh = lunesEndHour; em = lunesEndMinute; break;
            case DayOfWeek.Tuesday:
                enabled = martesEnabled; sh = martesStartHour; sm = martesStartMinute; eh = martesEndHour; em = martesEndMinute; break;
            case DayOfWeek.Wednesday:
                enabled = miercolesEnabled; sh = miercolesStartHour; sm = miercolesStartMinute; eh = miercolesEndHour; em = miercolesEndMinute; break;
            case DayOfWeek.Thursday:
                enabled = juevesEnabled; sh = juevesStartHour; sm = juevesStartMinute; eh = juevesEndHour; em = juevesEndMinute; break;
            case DayOfWeek.Friday:
                enabled = viernesEnabled; sh = viernesStartHour; sm = viernesStartMinute; eh = viernesEndHour; em = viernesEndMinute; break;
            case DayOfWeek.Saturday:
                enabled = sabadoEnabled; sh = sabadoStartHour; sm = sabadoStartMinute; eh = sabadoEndHour; em = sabadoEndMinute; break;
            default:
                enabled = false; sh = sm = eh = em = 0; break;
        }
    }

    private TimeSpan GetSpainOffset(DateTime utc)
    {
        // DST approx: last Sunday of March to last Sunday of October
        DateTime startDST = new DateTime(utc.Year, 3, 31);
        while (startDST.DayOfWeek != DayOfWeek.Sunday) startDST = startDST.AddDays(-1);
        DateTime endDST = new DateTime(utc.Year, 10, 31);
        while (endDST.DayOfWeek != DayOfWeek.Sunday) endDST = endDST.AddDays(-1);
        return (utc >= startDST && utc < endDST) ? new TimeSpan(2, 0, 0) : new TimeSpan(1, 0, 0);
    }
}
