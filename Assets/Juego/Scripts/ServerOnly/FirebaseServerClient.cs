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
            yield return new WaitForSeconds(0.1f);

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

        // Paso 2: Obtener y consumir tickets desde wallets/{walletAddress}
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

        if (tickets <= 0)
        {
            Debug.LogWarning("[Firebase] El jugador no tiene tickets disponibles.");
            callback(false);
            yield break;
        }

        // PATCH: Restar uno
        string patchJson = $"{{\"fields\":{{\"gameBalance\":{{\"mapValue\":{{\"fields\":{{\"ticketsAvailable\":{{\"integerValue\":\"{tickets - 1}\"}}}}}}}}}}}}";
        UnityWebRequest patch = new UnityWebRequest(walletUrl, "PATCH");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(patchJson);
        patch.uploadHandler = new UploadHandlerRaw(body);
        patch.downloadHandler = new DownloadHandlerBuffer();
        patch.SetRequestHeader("Content-Type", "application/json");
        patch.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return patch.SendWebRequest();

        bool success = patch.result == UnityWebRequest.Result.Success;
        Debug.Log(success
            ? $"[Firebase] Ticket consumido correctamente. Nuevo total = {tickets - 1}"
            : $"[Firebase] Error al consumir ticket: {patch.downloadHandler.text}");

        callback(success);
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

        // Paso 1: Obtener walletAddress desde users/{uid}
        string userUrl = GetUserUrl(uid);
        UnityWebRequest getUser = UnityWebRequest.Get(userUrl);
        getUser.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return getUser.SendWebRequest();

        if (getUser.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[Firebase] Error obteniendo usuario: " + getUser.downloadHandler.text);
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

        // Paso 2: Obtener current basic keys
        string walletUrl = GetWalletUrl(walletAddress);
        UnityWebRequest getWallet = UnityWebRequest.Get(walletUrl);
        getWallet.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return getWallet.SendWebRequest();

        if (getWallet.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[Firebase] Error obteniendo wallet: " + getWallet.downloadHandler.text);
            callback(false);
            yield break;
        }

        var walletData = JSON.Parse(getWallet.downloadHandler.text);
        int currentBasicKeys = walletData["fields"]["gameBalance"]["mapValue"]["fields"]["keys"]["mapValue"]["fields"]["basic"]["integerValue"].AsInt;

        // PATCH: Sumar una llave básica
        string patchJson = $@"{{ ""fields"": {{ ""gameBalance"": {{ ""mapValue"": {{ ""fields"": {{ ""keys"": {{ ""mapValue"": {{ ""fields"": {{ ""basic"": {{ ""integerValue"": ""{currentBasicKeys + 1}"" }}}}}}}}}}}}}}}}}}";

        UnityWebRequest patch = new UnityWebRequest(walletUrl, "PATCH");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(patchJson);
        patch.uploadHandler = new UploadHandlerRaw(body);
        patch.downloadHandler = new DownloadHandlerBuffer();
        patch.SetRequestHeader("Content-Type", "application/json");
        patch.SetRequestHeader("Authorization", $"Bearer {idToken}");

        yield return patch.SendWebRequest();

        bool success = patch.result == UnityWebRequest.Result.Success;
        Debug.Log(success
            ? $"[Firebase] Llave básica otorgada correctamente. Nuevo total = {currentBasicKeys + 1}"
            : $"[Firebase] Error al otorgar llave: {patch.downloadHandler.text}");

        callback(success);
    }


    public static IEnumerator UpdateRankedPoints(string uid, int pointsToAdd, Action<bool> callback)
    {
        string url = GetUserUrl(uid);

        var idToken = Instance.GetIdToken();

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

 #region Flujo_Nombre_ServerMirror_Firestore_Cliente

    /*public void Lol(string uid, string newName)
    {
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(newName))
        {
            Debug.LogWarning("[FirebaseServerClient] UID o nombre vacío. No se actualiza.");
            return;
        }

        StartCoroutine(UpdateNickname(uid, newName));
    }*/

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
