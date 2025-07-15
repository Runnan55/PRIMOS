using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using SimpleJSON;
using System.Linq;
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
        /*string idToken = WebGLStorage.LoadString("jwt_token");
        string uid = WebGLStorage.LoadString("local_id");*/

        string idToken = cachedToken;
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
        string updateMask = string.Join("&", fields.Keys.Select(k => $"updateMask.fieldPaths={k}"));
        string url = $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents/users/{uid}?{updateMask}";

        // Construir JSON con SimpleJSON
        var root = new JSONObject();
        var fieldsJson = new JSONObject();

        foreach (var kvp in fields)
        {
            var field = new JSONObject();

            if (kvp.Value is int || kvp.Value is long)
            {
                field["integerValue"] = kvp.Value.ToString();
            }
            else
            {
                field["stringValue"] = kvp.Value.ToString();
            }
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