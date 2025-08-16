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
        Debug.Log("[FirebaseServerClient] Awake ejecutado");

        StartCoroutine(WaitForAuthManagerAndLogin());
    }

    private IEnumerator WaitForAuthManagerAndLogin()
    {
        int retries = 30;
        while (AuthManager.Instance == null && retries-- > 0)
            yield return new WaitForSecondsRealtime(0.1f);

        if (AuthManager.Instance != null)
        {
            Debug.Log("[FirebaseServerClient] Llamando a login headless...");
            yield return AuthManager.Instance.StartCoroutine(AuthManager.Instance.LoginHeadlessForServer(adminEmail, adminPassword));
        }
        else
        {
            Debug.LogError("[FirebaseServerClient] AuthManager no disponible tras el timeout.");
        }
    }

    public static void SetServerCredentials(string token, string uid)
    {
        idToken = token;
        adminUid = uid;
        Debug.Log("[FirebaseServerClient] Token recibido directamente del AuthManager: " + token.Substring(0, 15) + "...");
        Debug.Log("[FirebaseServerClient] UID asignado al servidor: " + uid);
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
            Debug.Log($"[FirebaseServerClient] Puntos de {uid} actualizados.");
        else
            Debug.LogError("[FirebaseServerClient] Error actualizando puntos: " + req.downloadHandler.text);
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
            Debug.LogError("[Firebase] TryConsumeTicket: error getUser " + getUser.downloadHandler.text);
            callback(false);
            yield break;
        }

        var userData = JSON.Parse(getUser.downloadHandler.text);
        string walletAddress = userData["fields"]["walletAddress"]["stringValue"];
        if (string.IsNullOrEmpty(walletAddress))
        {
            Debug.LogError("[Firebase] TryConsumeTicket: walletAddress vacío.");
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
            Debug.LogError("[Firebase] TryConsumeTicket: error getWallet " + getWallet.downloadHandler.text);
            callback(false);
            yield break;
        }

        var walletData = JSON.Parse(getWallet.downloadHandler.text);
        int tickets = walletData["fields"]["gameBalance"]["mapValue"]["fields"]["ticketsAvailable"]["integerValue"].AsInt;

        if (tickets <= 0)
        {
            Debug.LogWarning("[Firebase] TryConsumeTicket: sin tickets.");
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
        if (!ok) Debug.LogError("[Firebase] TryConsumeTicket PATCH error: " + patch.downloadHandler.text);
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
            Debug.LogError("[Firebase] Error al obtener documento del usuario: " + getUser.downloadHandler.text);
            callback(false);
            yield break;
        }

        var userData = JSON.Parse(getUser.downloadHandler.text);
        string walletAddress = userData["fields"]["walletAddress"]?["stringValue"];
        if (string.IsNullOrEmpty(walletAddress))
        {
            Debug.LogError("[Firebase] walletAddress no encontrado para UID: " + uid);
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
            Debug.LogError("[Firebase] Error al obtener wallet: " + getWallet.downloadHandler.text);
            callback(false);
            yield break;
        }

        var walletData = JSON.Parse(getWallet.downloadHandler.text);
        int tickets = walletData["fields"]["gameBalance"]["mapValue"]["fields"]["ticketsAvailable"]["integerValue"].AsInt;

        Debug.Log($"[Firebase] CheckTicketAvailable: Wallet {walletAddress}, tickets = {tickets}");

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
            Debug.LogError("[Firebase] Error al obtener tickets y llaves: " + getUser.downloadHandler.text);
            callback(0, 0);
            yield break;
        }

        var data = JSON.Parse(getUser.downloadHandler.text);
        string walletAddress = data["fields"]["walletAddress"]["stringValue"];
        if (string.IsNullOrEmpty(walletAddress))
        {
            Debug.LogError("[Firebase] walletAddress no encontrado.");
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
            Debug.LogError("[Firebase] Error obteniendo wallet: " + getWallet.downloadHandler.text);
            callback(0, 0);
            yield break;
        }

        var walletData = JSON.Parse(getWallet.downloadHandler.text);
        int tickets = walletData["fields"]["gameBalance"]["mapValue"]["fields"]["ticketsAvailable"]["integerValue"].AsInt;
        int basicKeys = walletData["fields"]["gameBalance"]["mapValue"]["fields"]["keys"]["mapValue"]["fields"]["basic"]["integerValue"].AsInt;

        Debug.Log($"[Firebase] Tickets: {tickets}, basicKeys: {basicKeys} para wallet: {walletAddress}");

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
            Debug.LogError("[Firebase] GrantKeyToPlayer: error getUser " + getUser.downloadHandler.text);
            callback(false);
            yield break;
        }

        var userData = JSON.Parse(getUser.downloadHandler.text);
        string walletAddress = userData["fields"]["walletAddress"]["stringValue"];
        if (string.IsNullOrEmpty(walletAddress))
        {
            Debug.LogError("[Firebase] GrantKeyToPlayer: walletAddress vacío.");
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
            Debug.LogError("[Firebase] GrantKeyToPlayer: error getWallet " + getWallet.downloadHandler.text);
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
        if (!ok) Debug.LogError("[Firebase] GrantKeyToPlayer PATCH error: " + patch.downloadHandler.text);

        callback(ok);
    }



    public static IEnumerator UpdateRankedPoints(string uid, int pointsToAdd, Action<bool> callback)
    {
        string url = GetUserUrl(uid) + "?updateMask.fieldPaths=rankedPoints";
        var idToken = Instance.GetIdToken();

        // GET (usa baseUrl)
        UnityWebRequest getReq = UnityWebRequest.Get(url);
        getReq.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return getReq.SendWebRequest();

        if (getReq.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[Firebase] Error obteniendo rankedPoints: " + getReq.downloadHandler.text);
            callback(false);
            yield break;
        }

        var data = JSON.Parse(getReq.downloadHandler.text);
        int current = data["fields"]["rankedPoints"]["integerValue"].AsInt;

        // PATCH (usa patchUrl)
        string json = $"{{\"fields\":{{\"rankedPoints\":{{\"integerValue\":\"{current + pointsToAdd}\"}}}}}}";

        UnityWebRequest patch = new UnityWebRequest(url, "PATCH");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        patch.uploadHandler = new UploadHandlerRaw(body);
        patch.downloadHandler = new DownloadHandlerBuffer();
        patch.SetRequestHeader("Content-Type", "application/json");
        patch.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return patch.SendWebRequest();
        callback(patch.result == UnityWebRequest.Result.Success);
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
            Debug.LogError("[Firebase] (EnsureRankedPoints) GET error: " + getReq.downloadHandler.text);
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
            Debug.LogError("[Firebase] (EnsureRankedPoints) PATCH error: " + patch.downloadHandler.text);

        callback?.Invoke(0);
    }

    // Devuelve un JSON string con el Top-100: [{ "name": "...", "points": 123 }, ...]
    public static IEnumerator FetchTop100Leaderboard(string requesterUid, Action<string> onJsonReady)
    {
        // 1) Garantizar que el que solicita tenga rankedPoints
        yield return EnsureRankedPointsField(requesterUid, _ => { });

        // 2) Descargar users (page grande) y ordenar localmente por rankedPoints desc
        var idToken = Instance.GetIdToken();
        string url = GetUsersCollectionUrl(1000);
        var req = UnityWebRequest.Get(url);
        req.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return req.SendWebRequest();

        var arr = new JSONArray();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[Firebase] Leaderboard GET error: " + req.downloadHandler.text);
            onJsonReady?.Invoke(arr.ToString());
            yield break;
        }

        var root = JSON.Parse(req.downloadHandler.text);
        var docs = root["documents"]?.AsArray;
        if (docs == null) { onJsonReady?.Invoke(arr.ToString()); yield break; }

        var rows = new List<(string name, int points)>(docs.Count);
        foreach (var d in docs)
        {
            var doc = d.Value;
            string name = doc["fields"]?["nickname"]?["stringValue"] ?? "Unknown";
            int points = doc["fields"]?["rankedPoints"]?["integerValue"].AsInt ?? 0;
            rows.Add((name, points));
        }
        rows.Sort((a, b) => b.points.CompareTo(a.points));

        int count = Math.Min(100, rows.Count);
        for (int i = 0; i < count; i++)
        {
            var o = new JSONObject();
            o["name"] = rows[i].name;
            o["points"] = rows[i].points;
            arr.Add(o);
        }

        onJsonReady?.Invoke(arr.ToString());
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
            Debug.Log($"[FirebaseServerClient] Nombre actualizado exitosamente en Firestore para UID {uid}: {newName}");
        }
        else
        {
            Debug.LogError($"[FirebaseServerClient] Error al actualizar nombre para UID {uid}: {req.downloadHandler.text}");
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
            Debug.LogError("[FirebaseServerClient] Error al obtener nickname: " + req.downloadHandler.text);
            callback?.Invoke(null);
            yield break;
        }

        var data = JSON.Parse(req.downloadHandler.text);
        string nickname = data["fields"]["nickname"]?["stringValue"];
        callback?.Invoke(nickname);
    }

    #endregion
}
