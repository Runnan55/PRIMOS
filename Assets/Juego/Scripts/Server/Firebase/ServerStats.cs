using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public sealed class ServerStats : MonoBehaviour
{
    public static ServerStats Instance { get; private set; }

    [Header("Config")]
    [SerializeField] private string timeZoneId = "Europe/Madrid";
    [SerializeField] private int flushSeconds = 60;

    // Daily state
    private HashSet<string> dau = new HashSet<string>();
    private int peakConcurrent = 0;
    private int currentConcurrent = 0;

    private int matchesCreated = 0;
    private int matchesRankedCreated = 0;
    private int matchesCasualCreated = 0;

    // 1..6 buckets
    private readonly int[] rankedP = new int[7];
    private readonly int[] casualP = new int[7];

    private int ticketsConsumed = 0;
    private int keysGranted = 0;

    private int kills = 0;
    private int rankedPointsDelta = 0;

    // Date
    private TimeZoneInfo tz;
    private string dateKey; // "YYYY-MM-DD"

    // Backoff
    private int flushErrors = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        DontDestroyOnLoad(gameObject);
    }

    private void Start() { Init(); }

    // ===== PUBLIC STATIC API =====
    public static void BootOnce()
    {
        if (Instance != null) return;
        var go = new GameObject("ServerStats");
        Instance = go.AddComponent<ServerStats>();
        DontDestroyOnLoad(go);
        Instance.Init();
    }

    public static void FirstConnection(string uid)
    {
        if (Instance == null || string.IsNullOrEmpty(uid)) return;
        Instance.Roll();
        Instance.dau.Add(uid);
    }

    public static void ConnectionCountChanged(int connectedNow)
    {
        if (Instance == null) return;
        Instance.Roll();
        Instance.currentConcurrent = Mathf.Max(0, connectedNow);
        if (Instance.currentConcurrent > Instance.peakConcurrent) Instance.peakConcurrent = Instance.currentConcurrent;
    }

    public static void MatchCreated(bool ranked, int startingHumans)
    {
        if (Instance == null) return;
        Instance.Roll();
        Instance.matchesCreated++;
        int c = Mathf.Clamp(startingHumans, 1, 6);
        if (ranked)
        {
            Instance.matchesRankedCreated++;
            Instance.rankedP[c]++;
        }
        else
        {
            Instance.matchesCasualCreated++;
            Instance.casualP[c]++;
        }
    }

    public static void TicketConsumed()
    {
        if (Instance == null) return;
        Instance.Roll();
        Instance.ticketsConsumed++;
    }

    public static void KeyGranted()
    {
        if (Instance == null) return;
        Instance.Roll();
        Instance.keysGranted++;
    }

    public static void MatchEnded(int killsDelta, int rankedPointsDeltaDelta)
    {
        if (Instance == null) return;
        Instance.Roll();
        Instance.kills += Mathf.Max(0, killsDelta);
        Instance.rankedPointsDelta += rankedPointsDeltaDelta; // can be negative
    }

    // ===== INTERNALS =====
    private void Init()
    {
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch { tz = TimeZoneInfo.Utc; }
        dateKey = TodayKey();
        InvokeRepeating(nameof(FlushTick), flushSeconds, flushSeconds);
    }

    private string TodayKey()
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return local.ToString("yyyy-MM-dd");
    }

    private void Roll()
    {
        string t = TodayKey();
        if (t == dateKey) return;
        _ = FlushDaily(true);
        ResetDay(t);
    }

    private void ResetDay(string newKey)
    {
        dateKey = newKey;
        dau.Clear();
        peakConcurrent = 0;
        currentConcurrent = 0;
        matchesCreated = 0;
        matchesRankedCreated = 0;
        matchesCasualCreated = 0;
        Array.Clear(rankedP, 0, rankedP.Length);
        Array.Clear(casualP, 0, casualP.Length);
        ticketsConsumed = 0;
        keysGranted = 0;
        kills = 0;
        rankedPointsDelta = 0;
        flushErrors = 0;
    }

    private async void FlushTick() { await FlushDaily(false); }

    private async Task FlushDaily(bool force)
    {
        // skip until token ready
        if (FirebaseServerClient.Instance == null)
        {
            Debug.LogWarning("[ServerStats] Skip flush: FirebaseServerClient.Instance is null");
            return;
        }
        var token = FirebaseServerClient.Instance.GetIdToken();
        if (string.IsNullOrEmpty(token))
        {
            Debug.LogWarning("[ServerStats] Skip flush: idToken is empty");
            return;
        }

        var daily = new Dictionary<string, object>
        {
            { "tz", tz.Id },
            { "dau", dau.Count },
            { "peakConcurrent", peakConcurrent },
            { "matches", new Dictionary<string, object> {
                { "created", matchesCreated },
                { "ranked",  new Dictionary<string, object> { { "created", matchesRankedCreated } } },
                { "casual",  new Dictionary<string, object> { { "created", matchesCasualCreated } } },
            }},
            { "playersPerMatch", new Dictionary<string, object> {
                { "ranked", new Dictionary<string, object> {
                    { "p1", rankedP[1] }, { "p2", rankedP[2] }, { "p3", rankedP[3] },
                    { "p4", rankedP[4] }, { "p5", rankedP[5] }, { "p6", rankedP[6] },
                }},
                { "casual", new Dictionary<string, object> {
                    { "p1", casualP[1] }, { "p2", casualP[2] }, { "p3", casualP[3] },
                    { "p4", casualP[4] }, { "p5", casualP[5] }, { "p6", casualP[6] },
                }},
            }},
            { "tickets", new Dictionary<string, object> { { "consumed", ticketsConsumed } } },
            { "keys",    new Dictionary<string, object> { { "granted",  keysGranted } } },
            { "gameplay", new Dictionary<string, object> {
                { "kills", kills },
                { "rankedPointsDelta", rankedPointsDelta },
            }},
            { "updatedAt", new Dictionary<string, object> { { "_serverTimestamp", true } } }
        };

        try
        {
            await WriteDailyMerge(dateKey, daily);

            var agg = new Dictionary<string, object>
            {
                { "totalUsersSeen", dau.Count },
                { "totalMatchesCreated", matchesCreated },
                { "totalKeysGranted", keysGranted },
                { "totalTicketsConsumed", ticketsConsumed },
                { "updatedAt", new Dictionary<string, object> { { "_serverTimestamp", true } } }
            };
            await WriteAggregateMerge(agg);

            flushErrors = 0;
        }
        catch (Exception ex)
        {
            flushErrors++;
            int delay = Math.Min(30000, 500 * (1 << Math.Min(6, flushErrors)));
            Debug.LogWarning($"ServerStats flush error: {ex.Message}. retry in {delay} ms");
            await Task.Delay(delay);
        }
    }

    // === FIRESTORE I/O (implementa estas 2 segun tu FirebaseServerClient) ===
    private Task WriteDailyMerge(string date, Dictionary<string, object> data)
    {
        // PATCH a stats/daily/{date}
        string url = $"https://firestore.googleapis.com/v1/projects/primosminigameshoot/databases/(default)/documents/stats/daily/{date}";
        // No hace falta crear el doc antes: Firestore lo crea en PATCH si no existe.

        // updateMask con las raices principales para merge "barato"
        string[] masks = new[] { "tz", "dau", "peakConcurrent", "matches", "playersPerMatch", "tickets", "keys", "gameplay", "updatedAt" };
        for (int i = 0; i < masks.Length; i++)
            url += (i == 0 ? "?" : "&") + "updateMask.fieldPaths=" + UnityWebRequest.EscapeURL(masks[i]);

        // construimos JSON firestore-style
        var root = new SimpleJSON.JSONObject();
        var fields = new SimpleJSON.JSONObject();

        {
            var tzObj = new SimpleJSON.JSONObject();
            tzObj["stringValue"] = (string)data["tz"];
            fields["tz"] = tzObj;

            var dauObj = new SimpleJSON.JSONObject();
            dauObj["integerValue"] = ((int)data["dau"]).ToString();
            fields["dau"] = dauObj;

            var peakObj = new SimpleJSON.JSONObject();
            peakObj["integerValue"] = ((int)data["peakConcurrent"]).ToString();
            fields["peakConcurrent"] = peakObj;
        }

        // helper para map
        SimpleJSON.JSONObject ToMap(Dictionary<string, object> map)
        {
            var m = new SimpleJSON.JSONObject();
            var f = new SimpleJSON.JSONObject();
            foreach (var kv in map)
            {
                if (kv.Value is int iv)
                {
                    var v = new SimpleJSON.JSONObject(); v["integerValue"] = iv.ToString(); f[kv.Key] = v;
                }
                else if (kv.Value is Dictionary<string, object> sub)
                {
                    f[kv.Key] = ToMap(sub);
                }
            }
            var mapValue = new SimpleJSON.JSONObject();
            mapValue["fields"] = f;
            m["mapValue"] = mapValue;

            return m;
        }

        fields["matches"] = ToMap((Dictionary<string, object>)data["matches"]);
        fields["playersPerMatch"] = ToMap((Dictionary<string, object>)data["playersPerMatch"]);
        fields["tickets"] = ToMap((Dictionary<string, object>)data["tickets"]);
        fields["keys"] = ToMap((Dictionary<string, object>)data["keys"]);
        fields["gameplay"] = ToMap((Dictionary<string, object>)data["gameplay"]);

        // updatedAt sentinel (lo resolvemos en cliente: aqui solo guardamos now)
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var updateAt = new SimpleJSON.JSONObject(); updateAt["integerValue"] = ts.ToString();
        fields["updatedAt"] = updateAt;

        root["fields"] = fields;

        byte[] body = System.Text.Encoding.UTF8.GetBytes(root.ToString());
        var req = new UnityEngine.Networking.UnityWebRequest(url, "PATCH");
        req.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(body);
        req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", $"Bearer {FirebaseServerClient.Instance.GetIdToken()}");

        var op = req.SendWebRequest();
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        op.completed += _ => tcs.SetResult(true);
        return tcs.Task;
    }


    private Task WriteAggregateMerge(Dictionary<string, object> data)
    {
        string url = "https://firestore.googleapis.com/v1/projects/primosminigameshoot/databases/(default)/documents:commit";

        int users = (int)data["totalUsersSeen"];
        int matches = (int)data["totalMatchesCreated"];
        int keys = (int)data["totalKeysGranted"];
        int tickets = (int)data["totalTicketsConsumed"];

        // one write with update + fieldTransforms (increment)
        var root = new SimpleJSON.JSONObject();
        var writes = new SimpleJSON.JSONArray();

        var write = new SimpleJSON.JSONObject();

        // the "update" target doc: stats/aggregate
        var update = new SimpleJSON.JSONObject();
        update["name"] = $"projects/primosminigameshoot/databases/(default)/documents/stats/aggregate";

        // we can also set updatedAt to now (optional)
        var fields = new SimpleJSON.JSONObject();
        var updAt = new SimpleJSON.JSONObject(); updAt["integerValue"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        fields["updatedAt"] = updAt;
        update["fields"] = fields;
        write["update"] = update;

        // transforms
        var transforms = new SimpleJSON.JSONArray();
        SimpleJSON.JSONObject Inc(string fieldPath, int by)
        {
            var ft = new SimpleJSON.JSONObject();
            ft["fieldPath"] = fieldPath;
            var inc = new SimpleJSON.JSONObject();
            inc["integerValue"] = by.ToString();
            ft["increment"] = inc;
            return ft;
        }
        transforms.Add(Inc("totalUsersSeen", users));
        transforms.Add(Inc("totalMatchesCreated", matches));
        transforms.Add(Inc("totalKeysGranted", keys));
        transforms.Add(Inc("totalTicketsConsumed", tickets));

        write["updateTransforms"] = transforms;
        writes.Add(write);
        root["writes"] = writes;

        byte[] body = System.Text.Encoding.UTF8.GetBytes(root.ToString());
        var req = new UnityEngine.Networking.UnityWebRequest(url, "POST");
        req.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(body);
        req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", $"Bearer {FirebaseServerClient.Instance.GetIdToken()}");

        var op = req.SendWebRequest();
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        op.completed += _ => tcs.SetResult(true);
        return tcs.Task;
    }

}
