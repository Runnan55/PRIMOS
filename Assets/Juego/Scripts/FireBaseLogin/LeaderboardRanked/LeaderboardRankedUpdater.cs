using System.Collections;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;

public class LeaderboardRankedUpdater : MonoBehaviour
{
    public static LeaderboardRankedUpdater Instance;
    [SerializeField] private string firebaseProjectId = "primosminigamesshoot";

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void AddPointsToFirestore(string uid, int pointsToAdd)
    {
        string idToken = WebGLStorage.LoadString("jwt_token"); // o pasarlo si estás en server
        StartCoroutine(UpdatePointsCoroutine(idToken, uid, pointsToAdd));
    }

    private IEnumerator UpdatePointsCoroutine(string idToken, string uid, int pointsToAdd)
    {
        string url = $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents/users/{uid}";
        UnityWebRequest getRequest = UnityWebRequest.Get(url);
        getRequest.SetRequestHeader("Authorization", "Bearer " + idToken);
        yield return getRequest.SendWebRequest();

        if (getRequest.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Error obteniendo datos:" + getRequest.downloadHandler.text);
            yield break;
        }

        var response = JSON.Parse(getRequest.downloadHandler.text);
        int currentPoints = response["fields"]["rankedPoints"]["integerValue"].AsInt;

        int newTotalPoints = currentPoints + pointsToAdd;

        var root = new JSONObject();
        var fields = new JSONObject();
        fields["rankedPoints"] = new JSONObject { ["integerValue"] = newTotalPoints.ToString() };
        root["fields"] = fields;

        string payload = root.ToString();

        UnityWebRequest patchRequest = new UnityWebRequest(url, "PATCH");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(payload);
        patchRequest.uploadHandler = new UploadHandlerRaw(body);
        patchRequest.downloadHandler = new DownloadHandlerBuffer();
        patchRequest.SetRequestHeader("Content-Type", "application/json");
        patchRequest.SetRequestHeader("Authorization", "Bearer " + idToken);

        yield return patchRequest.SendWebRequest();

        if (patchRequest.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Error al guardar puntos: " + patchRequest.downloadHandler.text);
        }
        else
        {
            Debug.Log("Puntos actualizados con éxito.");
        }
    }


}
