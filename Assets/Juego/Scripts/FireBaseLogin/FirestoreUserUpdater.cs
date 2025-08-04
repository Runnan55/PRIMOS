using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using SimpleJSON;
using System.Linq;
using System;
using Mirror;

public class FirestoreUserUpdater : MonoBehaviour
{
    private static FirestoreUserUpdater instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[FirestoreUserUpdater] Ya existe una instancia, destruyendo duplicado.");
            Destroy(this.gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(this.gameObject);
        Debug.Log("[FirestoreUserUpdater] Instancia persistente registrada.");
    }

    [Header("Firebase Project ID")]
    [SerializeField] private string firebaseProjectId = "primosminigameshoot";
    [SerializeField] private string cachedToken;
    [SerializeField] private string cachedUID;

private Dictionary<NetworkConnectionToClient, FirebaseCredentials> credentialsMap = new();

public void SetCredentialsFor(NetworkConnectionToClient conn, FirebaseCredentials creds)
{
    credentialsMap[conn] = creds;
    Debug.Log($"[FirestoreUserUpdater] Credenciales almacenadas para {conn.identity?.netId} - UID: {creds.uid}");
}

public bool TryGetCredentials(NetworkConnectionToClient conn, out FirebaseCredentials creds)
{
    return credentialsMap.TryGetValue(conn, out creds);
}

    private void Start()
    {
        cachedToken = GetServerToken(out cachedUID);

        Debug.Log("[FirestoreUserUpdater] Token y UID cargados en Start:");
        Debug.Log($"  token = {cachedToken}");
        Debug.Log($"  uid = {cachedUID}");
    }

    private string GetServerToken(out string uid)
    {
#if UNITY_SERVER
        uid = null;
        var conn = NetworkServer.connections.FirstOrDefault().Value;
        if (AccountManager.Instance.TryGetFirebaseCredentials(conn, out var creds))
        {
            uid = creds.uid;
            return creds.idToken;
        }
        return null;
#else
    uid = null;
    return null;
#endif
    }

    public void UpdateUserData(Dictionary<string, object> fieldsToUpdate, System.Action<string> callback = null)
    {
#if UNITY_SERVER
    string uid;
    string idToken = GetServerToken(out uid);
#else
        string idToken = WebGLStorage.LoadString("jwt_token");
        string uid = WebGLStorage.LoadString("local_id");
#endif

        if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(uid))
        {
            callback?.Invoke("No hay sesión activa.");
            return;
        }

        StartCoroutine(SendPatchToFirestore(idToken, uid, fieldsToUpdate, callback));
    }

    private IEnumerator SendPatchToFirestore(string idToken, string uid, Dictionary<string, object> fields, System.Action<string> callback)
    {
        // Si vamos a acumular puntos, hacemos una petición previa
        if (fields.ContainsKey("rankedPoints"))
        {
            string getUrl = $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents/users/{uid}";

            UnityWebRequest getRequest = UnityWebRequest.Get(getUrl);
            getRequest.SetRequestHeader("Authorization", "Bearer " + idToken);
            yield return getRequest.SendWebRequest();

            if (getRequest.result == UnityWebRequest.Result.Success)
            {
                var json = JSON.Parse(getRequest.downloadHandler.text);
                int current = json["fields"]["rankedPoints"]?["integerValue"].AsInt ?? 0;
                int toAdd = fields["rankedPoints"] is int i ? i : int.Parse(fields["rankedPoints"].ToString());
                fields["rankedPoints"] = current + toAdd;
            }
            else
            {
                Debug.LogWarning("[Firestore] No se pudo obtener rankedPoints actuales, se usará el valor sin sumar.");
            }
        }

        // Armado del updateMask
        string updateMask = string.Join("&", fields.Keys.Select(k => $"updateMask.fieldPaths={k}"));
        string url = $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents/users/{uid}?{updateMask}";

        // Construcción del JSON
        var root = new JSONObject();
        var fieldsJson = new JSONObject();

        foreach (var kvp in fields)
        {
            var field = new JSONObject();
            if (kvp.Value is int or long)
                field["integerValue"] = kvp.Value.ToString();
            else
                field["stringValue"] = kvp.Value.ToString();

            fieldsJson[kvp.Key] = field;
        }

        root["fields"] = fieldsJson;
        string jsonPayload = root.ToString();

        Debug.Log($"[FirestoreUserUpdater] PATCH url: {url} — Payload: {jsonPayload}");
        UnityWebRequest request = new UnityWebRequest(url, "PATCH");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + idToken);

        yield return request.SendWebRequest();
        Debug.Log($"[FirestoreUserUpdater] PATCH response: {request.responseCode} - {request.downloadHandler.text}");

        if (request.result != UnityWebRequest.Result.Success)
        {
            callback?.Invoke("Error al guardar datos: " + request.downloadHandler.text);
        }
        else
        {
            callback?.Invoke("Datos guardados correctamente.");
        }
    }

#if UNITY_SERVER
    public void UpdateUserDataFor(NetworkConnectionToClient conn, Dictionary<string, object> fieldsToUpdate, System.Action<string> callback = null)
    {
        if (!AccountManager.Instance.TryGetFirebaseCredentials(conn, out var creds))
        {
            Debug.LogWarning("[FirestoreUserUpdater] No se encontraron credenciales para esta conexión.");
            callback?.Invoke("Sin credenciales.");
            return;
        }

        StartCoroutine(SendPatchToFirestore(creds.idToken, creds.uid, fieldsToUpdate, callback));
    }
#endif
}