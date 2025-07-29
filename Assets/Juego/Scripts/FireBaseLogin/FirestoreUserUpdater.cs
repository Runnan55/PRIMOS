using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using SimpleJSON;
using System.Linq;
using System;
//using UnityEditor.ShaderGraph.Serialization;

public class FirestoreUserUpdater : MonoBehaviour
{
    [Header("Firebase Project ID")]
    [SerializeField] private string firebaseProjectId = "primosminigameshoot";
    [SerializeField] private string cachedToken;
    [SerializeField] private string cachedUID;

    private void Start()
    {
        // Cargar una sola vez desde WebGLStorage
        cachedToken = WebGLStorage.LoadString("jwt_token");
        cachedUID = WebGLStorage.LoadString("local_id");

        Debug.Log("[FirestoreUserUpdater] Token y UID cargados en Start:");
        Debug.Log($"  token = {cachedToken}");
        Debug.Log($"  uid = {cachedUID}");
    }

    public void UpdateUserData(Dictionary<string, object> fieldsToUpdate, System.Action<string> callback = null)
    {
        string idToken = WebGLStorage.LoadString("jwt_token");//cachedToken;
        string uid = cachedUID;

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

        UnityWebRequest request = new UnityWebRequest(url, "PATCH");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + idToken);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            callback?.Invoke("Error al guardar datos: " + request.downloadHandler.text);
        }
        else
        {
            callback?.Invoke("Datos guardados correctamente.");
        }
    }

}