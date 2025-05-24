using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using SimpleJSON;
//using UnityEditor.ShaderGraph.Serialization;

public class FirestoreUserUpdater : MonoBehaviour
{
    [Header("Firebase Project ID")]
    [SerializeField] private string firebaseProjectId = "primosminigameshoot";

    public void UpdateUserData(Dictionary<string, string> fieldsToUpdate, System.Action<string> callback = null)
    {
        string idToken = WebGLStorage.LoadString("jwt_token");
        string uid = WebGLStorage.LoadString("local_id");

        if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(uid))
        {
            callback?.Invoke("No hay sesión activa.");
            return;
        }

        StartCoroutine(SendPatchToFirestore(idToken, uid, fieldsToUpdate, callback));
    }

    private IEnumerator SendPatchToFirestore(string idToken, string uid, Dictionary<string, string> fields, System.Action<string> callback)
    {
        string url = $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents/users/{uid}";

        // Construir JSON con SimpleJSON
        var root = new JSONObject();
        var fieldsJson = new JSONObject();

        foreach (var kvp in fields)
        {
            var field = new JSONObject();
            field["stringValue"] = kvp.Value;
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