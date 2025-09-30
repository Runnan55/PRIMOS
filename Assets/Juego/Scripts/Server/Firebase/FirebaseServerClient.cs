using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using SimpleJSON;
using System;
using Unity.VisualScripting.Antlr3.Runtime;
using Mirror;

public class FirebaseServerClient : MonoBehaviour
{
    public static FirebaseServerClient Instance { get; private set; }

    [Header("Credenciales de cuenta admin")]
    [SerializeField] private string adminEmail = "PrimosMinigameShoot@gmail.com";
    [SerializeField] private string adminPassword = "Proyectoprimos@1234";

    private static string idToken;
    private static string adminUid;

    private const string FirebaseProjectId = "primosminigameshoot";

    public string GetIdToken() => idToken;

    private static string GetUserUrl(string uid) => $"https://firestore.googleapis.com/v1/projects/{FirebaseProjectId}/databases/(default)/documents/users/{uid}";

    private static string GetWalletUrl(string walletAddress) => $"https://firestore.googleapis.com/v1/projects/{FirebaseProjectId}/databases/(default)/documents/wallets/{walletAddress}";
    private static string GetUsersCollectionUrl(int pageSize = 1000)  => $"https://firestore.googleapis.com/v1/projects/{FirebaseProjectId}/databases/(default)/documents/users?pageSize={pageSize}";

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        DontDestroyOnLoad(gameObject);
#if UNITY_SERVER
        StartCoroutine(WaitForAuthManagerAndLogin()); // SOLO en build de servidor
#else
    LogWithTime.Log("[FirebaseServerClient] Client build: sin login headless.");
    Destroy(gameObject);
#endif
    }

#if UNITY_SERVER
    private IEnumerator WaitForAuthManagerAndLogin()
    {
        int retries = 30;
        while (AuthManager.Instance == null && retries-- > 0)
            yield return new WaitForSecondsRealtime(0.1f);

        if (AuthManager.Instance != null)
        {
            LogWithTime.Log("[FirebaseServerClient] Llamando a login headless...");
            yield return AuthManager.Instance.StartCoroutine(AuthManager.Instance.LoginHeadlessForServer(adminEmail, adminPassword));
        }
        else
        {
            LogWithTime.LogError("[FirebaseServerClient] AuthManager no disponible tras el timeout.");
        }
    }
#endif

    public static void SetServerCredentials(string token, string uid)
    {
        idToken = token;
        adminUid = uid;
        LogWithTime.Log("[FirebaseServerClient] Token recibido directamente del AuthManager: " + token.Substring(0, 15) + "...");
        LogWithTime.Log("[FirebaseServerClient] UID asignado al servidor: " + uid);
    }

    // Actualizar rankedPoints
    public static IEnumerator AddRankedPoints(string uid, int points)
    {
        string url = GetUserUrl(uid) + "?updateMask.fieldPaths=rankedPoints";

        var idToken = Instance.GetIdToken();

        // Esto sobrescribe el valor, no lo incrementa en Firestore. Alternativa: leer primero.
        string json = $"{{\"fields\":{{\"rankedPoints\":{{\"integerValue\":\"{points}\"}}}}}}";

        UnityWebRequest req = new UnityWebRequest(url, "PATCH");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            LogWithTime.Log($"[FirebaseServerClient] Puntos de {uid} actualizados.");
        else
            LogWithTime.LogError("[FirebaseServerClient] Error actualizando puntos: " + req.downloadHandler.text);
    }

    public static IEnumerator TryConsumeTicket(string uid, Action<bool> callback)
    {
        var idToken = Instance.GetIdToken();

        // 1) Leer user -> walletAddress
        string userUrl = GetUserUrl(uid);
        UnityWebRequest getUser = UnityWebRequest.Get(userUrl);
        getUser.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return getUser.SendWebRequest();

        if (getUser.result != UnityWebRequest.Result.Success)
        {
            LogWithTime.LogError("[Firebase] TryConsumeTicket: error getUser " + getUser.downloadHandler.text);
            callback(false);
            yield break;
        }

        var userData = JSON.Parse(getUser.downloadHandler.text);
        string walletAddress = userData["fields"]["walletAddress"]["stringValue"];
        if (string.IsNullOrEmpty(walletAddress))
        {
            LogWithTime.LogError("[Firebase] TryConsumeTicket: walletAddress vacío.");
            callback(false);
            yield break;
        }

        // 2) Leer wallet -> tickets
        string walletUrl = GetWalletUrl(walletAddress);
        UnityWebRequest getWallet = UnityWebRequest.Get(walletUrl);
        getWallet.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return getWallet.SendWebRequest();

        if (getWallet.result != UnityWebRequest.Result.Success)
        {
            LogWithTime.LogError("[Firebase] TryConsumeTicket: error getWallet " + getWallet.downloadHandler.text);
            callback(false);
            yield break;
        }

        var walletData = JSON.Parse(getWallet.downloadHandler.text);
        int tickets = walletData["fields"]["gameBalance"]["mapValue"]["fields"]["ticketsAvailable"]["integerValue"].AsInt;

        if (tickets <= 0)
        {
            LogWithTime.LogWarning("[Firebase] TryConsumeTicket: sin tickets.");
            callback(false);
            yield break;
        }

        int newTickets = tickets - 1;

        // 3) PATCH con updateMask a gameBalance.ticketsAvailable
        string patchUrl = walletUrl + "?updateMask.fieldPaths=gameBalance.ticketsAvailable";

        var payload = new JSONObject();
        var fields = new JSONObject();

        var gameBalance = new JSONObject();
        var gbMap = new JSONObject();
        var gbFields = new JSONObject();

        var ticketsField = new JSONObject();
        ticketsField["integerValue"] = (tickets - 1).ToString();
        gbFields["ticketsAvailable"] = ticketsField;

        gbMap["fields"] = gbFields;
        gameBalance["mapValue"] = gbMap;

        fields["gameBalance"] = gameBalance;
        payload["fields"] = fields;

        var body = System.Text.Encoding.UTF8.GetBytes(payload.ToString());
        var patch = new UnityWebRequest(patchUrl, "PATCH");
        patch.uploadHandler = new UploadHandlerRaw(body);
        patch.downloadHandler = new DownloadHandlerBuffer();
        patch.SetRequestHeader("Content-Type", "application/json");
        patch.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return patch.SendWebRequest();

        bool ok = (patch.result == UnityWebRequest.Result.Success);
        if (!ok) LogWithTime.LogError("[Firebase] TryConsumeTicket PATCH error: " + patch.downloadHandler.text);
        callback(ok);
    }

    public static IEnumerator CheckTicketAvailable(string uid, Action<bool> callback)
    {
        var idToken = Instance.GetIdToken();

        // Paso 1: Obtener walletAddress desde users/{uid}
        string userUrl = GetUserUrl(uid);
        UnityWebRequest getUser = UnityWebRequest.Get(userUrl);
        getUser.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return getUser.SendWebRequest();

        if (getUser.result != UnityWebRequest.Result.Success)
        {
            LogWithTime.LogError("[Firebase] Error al obtener documento del usuario: " + getUser.downloadHandler.text);
            callback(false);
            yield break;
        }

        var userData = JSON.Parse(getUser.downloadHandler.text);
        string walletAddress = userData["fields"]["walletAddress"]?["stringValue"];
        if (string.IsNullOrEmpty(walletAddress))
        {
            LogWithTime.LogError("[Firebase] walletAddress no encontrado para UID: " + uid);
            callback(false);
            yield break;
        }

        // Paso 2: Obtener tickets desde wallets/{walletAddress}
        string walletUrl = GetWalletUrl(walletAddress);
        UnityWebRequest getWallet = UnityWebRequest.Get(walletUrl);
        getWallet.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return getWallet.SendWebRequest();

        if (getWallet.result != UnityWebRequest.Result.Success)
        {
            LogWithTime.LogError("[Firebase] Error al obtener wallet: " + getWallet.downloadHandler.text);
            callback(false);
            yield break;
        }

        var walletData = JSON.Parse(getWallet.downloadHandler.text);
        int tickets = walletData["fields"]["gameBalance"]["mapValue"]["fields"]["ticketsAvailable"]["integerValue"].AsInt;

        LogWithTime.Log($"[Firebase] CheckTicketAvailable: Wallet {walletAddress}, tickets = {tickets}");

        callback(tickets > 0);
    }

    public static IEnumerator FetchTicketAndKeyInfoFromWallet(string uid, Action<int, int> callback)
    {
        var idToken = Instance.GetIdToken();

        // Paso 1: Obtener walletAddress desde users
        string userUrl = GetUserUrl(uid);
        UnityWebRequest getUser = UnityWebRequest.Get(userUrl);
        getUser.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return getUser.SendWebRequest();

        if (getUser.result != UnityWebRequest.Result.Success)
        {
            LogWithTime.LogError("[Firebase][uid=" + uid + "]  Error al obtener tickets/keys user: " + getUser.downloadHandler.text);
            callback(0, 0);
            yield break;
        }

        var data = JSON.Parse(getUser.downloadHandler.text);
        string walletAddress = data["fields"]["walletAddress"]["stringValue"];
        if (string.IsNullOrEmpty(walletAddress))
        {
            LogWithTime.LogError("[Firebase] walletAddress no encontrado.");
            callback(0, 0);
            yield break;
        }

        //Paso 2: Obtener tickets y keys desde wallets
        string walletUrl = GetWalletUrl(walletAddress);
        UnityWebRequest getWallet = UnityWebRequest.Get(walletUrl);
        getWallet.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return getWallet.SendWebRequest();

        if (getWallet.result != UnityWebRequest.Result.Success)
        {
            LogWithTime.LogError("[Firebase] Error obteniendo wallet: " + getWallet.downloadHandler.text);
            LogWithTime.LogError("[Firebase][uid=" + uid + "] Error obteniendo wallet: " + getWallet.downloadHandler.text);
            callback(0, 0);
            yield break;
        }

        var walletData = JSON.Parse(getWallet.downloadHandler.text);
        int tickets = walletData["fields"]["gameBalance"]["mapValue"]["fields"]["ticketsAvailable"]["integerValue"].AsInt;
        int basicKeys = walletData["fields"]["gameBalance"]["mapValue"]["fields"]["keys"]["mapValue"]["fields"]["basic"]["integerValue"].AsInt;

        callback(tickets, basicKeys);

    }

    public static IEnumerator GrantKeyToPlayer(string uid, Action<bool> callback)
    {
        var idToken = Instance.GetIdToken();

        // 1) Leer user -> walletAddress
        string userUrl = GetUserUrl(uid);
        UnityWebRequest getUser = UnityWebRequest.Get(userUrl);
        getUser.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return getUser.SendWebRequest();

        if (getUser.result != UnityWebRequest.Result.Success)
        {
            LogWithTime.LogError("[Firebase] GrantKeyToPlayer: error getUser " + getUser.downloadHandler.text);
            callback(false);
            yield break;
        }

        var userData = JSON.Parse(getUser.downloadHandler.text);
        string walletAddress = userData["fields"]["walletAddress"]["stringValue"];
        if (string.IsNullOrEmpty(walletAddress))
        {
            LogWithTime.LogError("[Firebase] GrantKeyToPlayer: walletAddress vacío.");
            callback(false);
            yield break;
        }

        // 2) Leer wallet -> keys.basic
        string walletUrl = GetWalletUrl(walletAddress);
        UnityWebRequest getWallet = UnityWebRequest.Get(walletUrl);
        getWallet.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return getWallet.SendWebRequest();

        if (getWallet.result != UnityWebRequest.Result.Success)
        {
            LogWithTime.LogError("[Firebase] GrantKeyToPlayer: error getWallet " + getWallet.downloadHandler.text);
            callback(false);
            yield break;
        }

        var walletData = JSON.Parse(getWallet.downloadHandler.text);
        int currentBasic = walletData["fields"]["gameBalance"]["mapValue"]["fields"]["keys"]["mapValue"]["fields"]["basic"]["integerValue"].AsInt;
        int newBasic = currentBasic + 1;

        // 3) PATCH con updateMask a gameBalance.keys.basic
        string patchUrl = walletUrl + "?updateMask.fieldPaths=gameBalance.keys.basic";

        var payload = new JSONObject();
        var fields = new JSONObject();

        var gameBalance = new JSONObject();
        var gbMap = new JSONObject();
        var gbFields = new JSONObject();

        var keys = new JSONObject();
        var keysMap = new JSONObject();
        var keysFields = new JSONObject();

        // newBasic = currentBasic + 1
        var basic = new JSONObject();
        basic["integerValue"] = newBasic.ToString();
        keysFields["basic"] = basic;

        keysMap["fields"] = keysFields;
        keys["mapValue"] = keysMap;

        gbFields["keys"] = keys;
        gbMap["fields"] = gbFields;
        gameBalance["mapValue"] = gbMap;

        fields["gameBalance"] = gameBalance;
        payload["fields"] = fields;

        var body = System.Text.Encoding.UTF8.GetBytes(payload.ToString());
        var patch = new UnityWebRequest(patchUrl, "PATCH");
        patch.uploadHandler = new UploadHandlerRaw(body);
        patch.downloadHandler = new DownloadHandlerBuffer();
        patch.SetRequestHeader("Content-Type", "application/json");
        patch.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return patch.SendWebRequest();

        bool ok = (patch.result == UnityWebRequest.Result.Success);
        if (!ok) LogWithTime.LogError("[Firebase] GrantKeyToPlayer PATCH error: " + patch.downloadHandler.text);

        callback(ok);
    }

    public static IEnumerator UpdateRankedPoints(string uid, int pointsToAdd, Action<bool> callback)
    {
        var getUrl = GetUserUrl(uid);                                             // Obtener datos a lo bestia sin restricciones
        var patchUrl = GetUserUrl(uid) + "?updateMask.fieldPaths=rankedPoints";   // Modificar solo el campo requerido, para eso usamos el updateMask

        var idToken = Instance.GetIdToken();

        // GET (usa baseUrl)
        UnityWebRequest getReq = UnityWebRequest.Get(getUrl);
        getReq.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return getReq.SendWebRequest();

        if (getReq.result != UnityWebRequest.Result.Success)
        {
            LogWithTime.LogError("[Firebase] Error obteniendo rankedPoints: " + getReq.downloadHandler.text);
            callback(false);
            yield break;
        }

        var data = JSON.Parse(getReq.downloadHandler.text);
        int current = data["fields"]["rankedPoints"]["integerValue"].AsInt;

        // PATCH (usa patchUrl)
        string json = $"{{\"fields\":{{\"rankedPoints\":{{\"integerValue\":\"{current + pointsToAdd}\"}}}}}}";

        UnityWebRequest patch = new UnityWebRequest(patchUrl, "PATCH");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        patch.uploadHandler = new UploadHandlerRaw(body);
        patch.downloadHandler = new DownloadHandlerBuffer();
        patch.SetRequestHeader("Content-Type", "application/json");
        patch.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return patch.SendWebRequest();

        bool success = (patch.result == UnityWebRequest.Result.Success);

        if (success)
        {
            LogWithTime.Log($"[Firebase][UpdateRP] Ranked points entregados -> uid={uid}, +{pointsToAdd}");
        }
        else
        {
            LogWithTime.LogError($"[Firebase][UpdateRP] PATCH error -> uid={uid}, +{pointsToAdd}, resp={patch.downloadHandler.text}");
        }

        callback(patch.result == UnityWebRequest.Result.Success);
    }

    public static IEnumerator SetHasPlayedRanked(string uid, bool value, Action<bool> callback = null)
    {
        var idToken = Instance.GetIdToken();
        string url = GetUserUrl(uid) + "?updateMask.fieldPaths=hasPlayedRanked";

        // booleanValue must be unquoted true/false
        string json = "{\"fields\":{\"hasPlayedRanked\":{\"booleanValue\":" + (value ? "true" : "false") + "}}}";

        var req = new UnityWebRequest(url, "PATCH");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return req.SendWebRequest();

        bool ok = (req.result == UnityWebRequest.Result.Success);
        if (!ok) Debug.LogError("[Firebase] SetHasPlayedRanked PATCH error: " + req.downloadHandler.text);
        callback?.Invoke(ok);
    }


    #region LeaderboardRankedPoint

    // Garantiza que el documento users/{uid} tenga rankedPoints; si falta, lo crea a 0.
    public static IEnumerator EnsureRankedPointsField(string uid, Action<int> callback)
    {
        var idToken = Instance.GetIdToken();
        string userUrl = GetUserUrl(uid);

        var getReq = UnityWebRequest.Get(userUrl);
        getReq.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return getReq.SendWebRequest();

        if (getReq.result != UnityWebRequest.Result.Success)
        {
            LogWithTime.LogError("[Firebase] (EnsureRankedPoints) GET error: " + getReq.downloadHandler.text);
            callback?.Invoke(0);
            yield break;
        }

        var json = JSON.Parse(getReq.downloadHandler.text);
        var rankedNode = json["fields"]?["rankedPoints"]?["integerValue"];
        if (rankedNode != null && !string.IsNullOrEmpty(rankedNode))
        {
            callback?.Invoke(rankedNode.AsInt);
            yield break;
        }

        string patchUrl = userUrl + "?updateMask.fieldPaths=rankedPoints";
        var body = new JSONObject();
        body["fields"] = new JSONObject();
        body["fields"]["rankedPoints"] = new JSONObject();
        body["fields"]["rankedPoints"]["integerValue"] = "0";

        var patch = new UnityWebRequest(patchUrl, "PATCH");
        var raw = System.Text.Encoding.UTF8.GetBytes(body.ToString());
        patch.uploadHandler = new UploadHandlerRaw(raw);
        patch.downloadHandler = new DownloadHandlerBuffer();
        patch.SetRequestHeader("Content-Type", "application/json");
        patch.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return patch.SendWebRequest();

        if (patch.result != UnityWebRequest.Result.Success)
            LogWithTime.LogError("[Firebase] (EnsureRankedPoints) PATCH error: " + patch.downloadHandler.text);

        callback?.Invoke(0);
    }

    // Devuelve: { "top":[{name,points}...], "self":{uid,name,points,rank} }
    public static IEnumerator FetchTop100Leaderboard(string requesterUid, Action<string> onJsonReady)
    {
        // Asegura que el solicitante tenga el campo rankedPoints
        yield return EnsureRankedPointsField(requesterUid, _ => { });

        var idToken = Instance.GetIdToken();

        // --- CONFIG ---
        const int PAGE_SIZE = 300;   // límite duro de Firestore REST
        const int SCAN_LIMIT = 1200;  // sube/baja esto: 300, 600, 900, 1200...
        const int TOP_N = 100;

        // --- Paginación: lee hasta SCAN_LIMIT usuarios ---
        var all = new List<(string uid, string name, int points)>(SCAN_LIMIT);
        string pageToken = null;
        int fetched = 0;

        do
        {
            string url = GetUsersCollectionUrl(PAGE_SIZE);
            if (!string.IsNullOrEmpty(pageToken)) url += $"&pageToken={pageToken}";

            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Authorization", $"Bearer {idToken}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LogWithTime.LogError("[Firebase] Leaderboard GET error: " + req.downloadHandler.text);
                var fail = new JSONObject();
                fail["top"] = new JSONArray();
                var selfFail = new JSONObject();
                selfFail["uid"] = requesterUid;
                selfFail["name"] = "You";
                selfFail["points"] = 0;
                selfFail["rank"] = -1;
                fail["self"] = selfFail;
                onJsonReady?.Invoke(fail.ToString());
                yield break;
            }

            var root = JSON.Parse(req.downloadHandler.text);
            var docs = root?["documents"]?.AsArray;

            if (docs != null)
            {
                foreach (var d in docs)
                {
                    var doc = d.Value;
                    string fullDocName = doc["name"]; // .../documents/users/{uid}
                    string uid = "";
                    if (!string.IsNullOrEmpty(fullDocName))
                    {
                        int slash = fullDocName.LastIndexOf('/');
                        uid = (slash >= 0 && slash + 1 < fullDocName.Length)
                            ? fullDocName.Substring(slash + 1)
                            : fullDocName;
                    }

                    string name = doc["fields"]?["nickname"]?["stringValue"];
                    if (string.IsNullOrWhiteSpace(name)) name = "Unknown";

                    int points = 0;
                    var pNode = doc["fields"]?["rankedPoints"]?["integerValue"];
                    if (pNode != null) points = pNode.AsInt;

                    // Filter by hasPlayedRanked == true
                    var playedNode = doc["fields"]?["hasPlayedRanked"]?["booleanValue"];
                    bool hasPlayed = false;
                    if (playedNode != null) hasPlayed = playedNode.AsBool;
                    if (!hasPlayed) continue;

                    all.Add((uid, name, points));
                    fetched++;
                    if (fetched >= SCAN_LIMIT) break;
                }
            }

            pageToken = root?["nextPageToken"];
            if (fetched >= SCAN_LIMIT) break;

        } while (!string.IsNullOrEmpty(pageToken));

        // --- Orden por puntos (desc). Empates: orden arbitrario estable por uid para consistencia.
        all.Sort((a, b) =>
        {
            int cmp = b.points.CompareTo(a.points);
            return (cmp != 0) ? cmp : string.CompareOrdinal(a.uid, b.uid);
        });

        // --- Top-N para la lista visible ---
        var result = new JSONObject();
        var topArr = new JSONArray();
        int topCount = Math.Min(TOP_N, all.Count);
        for (int i = 0; i < topCount; i++)
        {
            var o = new JSONObject();
            o["name"] = all[i].name;
            o["points"] = all[i].points;
            topArr.Add(o);
        }
        result["top"] = topArr;

        // --- Self (rank global simple: índice+1) ---
        int selfIdx = all.FindIndex(r => r.uid == requesterUid);
        var selfObj = new JSONObject();

        if (selfIdx >= 0)
        {
            var r = all[selfIdx];
            selfObj["uid"] = r.uid;
            selfObj["name"] = string.IsNullOrWhiteSpace(r.name) ? "Unknown" : r.name;
            selfObj["points"] = r.points;
            selfObj["rank"] = selfIdx + 1; // sin compartir puestos
        }
        else
        {
            selfObj["uid"] = requesterUid;
            selfObj["name"] = "You";
            selfObj["points"] = 0;
            selfObj["rank"] = -1;
        }

        result["self"] = selfObj;
        onJsonReady?.Invoke(result.ToString());
    }

    #endregion

    #region Flujo_Nombre_ServerMirror_Firestore_Cliente

    public static IEnumerator UpdateNickname(string uid, string newName)
    {
        var idToken = Instance.GetIdToken();

        string url = GetUserUrl(uid) + "?updateMask.fieldPaths=nickname";

        string json = $"{{\"fields\":{{\"nickname\":{{\"stringValue\":\"{newName}\"}}}}}}";

        UnityWebRequest req = new UnityWebRequest(url, "PATCH");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            LogWithTime.Log($"[FirebaseServerClient] Nombre actualizado exitosamente en Firestore para UID {uid}: {newName}");
        }
        else
        {
            LogWithTime.LogError($"[FirebaseServerClient] Error al actualizar nombre para UID {uid}: {req.downloadHandler.text}");
        }
    }

    public static IEnumerator GetNicknameFromFirestore(string uid, Action<string> callback)
    {
        var idToken = Instance.GetIdToken();

        string url = GetUserUrl(uid);
        UnityWebRequest req = UnityWebRequest.Get(url);
        req.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            LogWithTime.LogError("[FirebaseServerClient] Error al obtener nickname: " + req.downloadHandler.text);
            callback?.Invoke(null);
            yield break;
        }

        var data = JSON.Parse(req.downloadHandler.text);
        string nickname = data["fields"]["nickname"]?["stringValue"];
        callback?.Invoke(nickname);
    }

    #endregion

    #region --- Winrate (incremento con updateMask sobre subcampos concretos) ---

    public static IEnumerator IncrementWinrateAndTotals(string uid, int position)
    {
        // 1) GET users/{uid} para leer valores actuales (solo una vez)
        string getUrl = GetUserUrl(uid);
        var getReq = UnityWebRequest.Get(getUrl);
        getReq.SetRequestHeader("Authorization", $"Bearer {Instance.GetIdToken()}");
        yield return getReq.SendWebRequest();

        if (getReq.result != UnityWebRequest.Result.Success)
        {
            LogWithTime.LogError("[Firebase] IncrementWinrateAndTotals GET error: " + getReq.downloadHandler.text);
            yield break;
        }

        var json = JSON.Parse(getReq.downloadHandler.text);

        // Campo raiz y subcampos tal como estan en tu DB (respetar mayusculas!)
        string root = "WinrateRanked"; // si en tu coleccion es "winrate" cambia aqui
                                 // keys con espacios -> tal como en tu captura
        string k1 = position switch
        {
            1 => "1er puesto",
            2 => "2do puesto",
            3 => "3er puesto",
            4 => "4to puesto",
            5 => "5to puesto",
            _ => "6to puesto"
        };
        string kTot = "PartidasTotales";

        // Lectura segura (0 si no existe)
        int curPuesto = json["fields"]?[root]?["mapValue"]?["fields"]?[k1]?["integerValue"].AsInt ?? 0;
        int curTot = json["fields"]?[root]?["mapValue"]?["fields"]?[kTot]?["integerValue"].AsInt ?? 0;

        int newPuesto = curPuesto + 1;
        int newTot = curTot + 1;

        // 2) PATCH con updateMask SOLO de los 2 subcampos
        // OJO: nombres con espacios requieren backticks en fieldPaths.
        // Ej: updateMask.fieldPaths=Winrate.`1er puesto`
        string patchUrl =
            GetUserUrl(uid)
            + $"?updateMask.fieldPaths={Uri.EscapeDataString($"{root}.`{k1}`")}"
            + $"&updateMask.fieldPaths={Uri.EscapeDataString($"{root}.{kTot}")}";

        var bodyRoot = new JSONObject();
        var fields = new JSONObject();
        var winrate = new JSONObject();
        var mapValue = new JSONObject();
        var mapFlds = new JSONObject();

        var vPuesto = new JSONObject(); vPuesto["integerValue"] = newPuesto.ToString();
        var vTot = new JSONObject(); vTot["integerValue"] = newTot.ToString();

        mapFlds[k1] = vPuesto;
        mapFlds[kTot] = vTot;

        mapValue["fields"] = mapFlds;
        winrate["mapValue"] = mapValue;
        fields[root] = winrate;
        bodyRoot["fields"] = fields;

        byte[] body = System.Text.Encoding.UTF8.GetBytes(bodyRoot.ToString());

        var patch = new UnityWebRequest(patchUrl, "PATCH");
        patch.uploadHandler = new UploadHandlerRaw(body);
        patch.downloadHandler = new DownloadHandlerBuffer();
        patch.SetRequestHeader("Content-Type", "application/json");
        patch.SetRequestHeader("Authorization", $"Bearer {Instance.GetIdToken()}");

        yield return patch.SendWebRequest();

        if (patch.result != UnityWebRequest.Result.Success)
            LogWithTime.LogError("[Firebase] IncrementWinrateAndTotals PATCH error: " + patch.downloadHandler.text);
        else
            LogWithTime.Log($"[Firebase] Winrate actualizado uid={uid} pos={position} -> {k1}+1 y {kTot}+1");
    }

    #endregion
}
