using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using UnityEngine;
using TMPro;
using SimpleJSON;

public class FirestoreLeaderboardFetcher : MonoBehaviour
{
    public static FirestoreLeaderboardFetcher Instance;

    public Transform contentContainer;
    public GameObject leaderboardRankedEntryPrefab;
    public GameObject localPlayerRankedEntry;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void RefreshLeaderboard()
    {
        StartCoroutine(FetchAndDisplayLeaderboard());
    }

    public IEnumerator FetchAndDisplayLeaderboard()
    {
        string idToken = WebGLStorage.LoadString("jwt_token");
        string url = "https://firestore.googleapis.com/v1/projects/primosminigameshoot/databases/(default)/documents/users?pageSize=100";

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", "Bearer " + idToken);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Error al obtener leaderboard: " + request.downloadHandler.text);
            yield break;
        }

        var result = JSON.Parse(request.downloadHandler.text);
        var users = new List<UserEntry>();

        foreach (var doc in result["documents"].Values)
        {
            string nickname = doc["fields"]["nickname"]?["stringValue"] ?? "Unknown";
            int points = doc["fields"]["rankedPoints"]?["integerValue"].AsInt ?? 0;

            users.Add(new UserEntry { name = nickname, points = points });
        }

        // Ordenar usuarios por puntaje
        users.Sort((a, b) => b.points.CompareTo(a.points));

        // Limpiar contenido previo
        foreach (Transform child in contentContainer)
            Destroy(child.gameObject);

        // Instanciar entradas hasta el top 100
        for (int i = 0; i < users.Count && i < 100; i++)
        {
            var user = users[i];
            GameObject entry = Instantiate(leaderboardRankedEntryPrefab, contentContainer);

            var ui = entry.GetComponent<LeaderboardEntryUI>();
            if (ui != null)
                ui.SetEntry(i + 1, user.name, user.points);
        }

        // Obtener el nombre ingresado en el input
        string localName = FindFirstObjectByType<MainLobbyUI>()?.nameInputField?.text?.Trim();

        if (!string.IsNullOrEmpty(localName) && localPlayerRankedEntry != null)
        {
            int localRank = users.FindIndex(u => u.name.Trim().Equals(localName, System.StringComparison.OrdinalIgnoreCase));

            if (localRank != -1)
            {
                var user = users[localRank];
                localPlayerRankedEntry.SetActive(true);

                var ui = localPlayerRankedEntry.GetComponent<LeaderboardEntryUI>();
                if (ui != null)
                    ui.SetEntry(localRank + 1, user.name, user.points);
            }
            else
            {
                localPlayerRankedEntry.SetActive(true);
                var ui = localPlayerRankedEntry.GetComponent<LeaderboardEntryUI>();
                if (ui != null)
                    ui.SetEntry(-1, localName + " (No Rank)", 0); // Muestra nombre aunque no esté rankeado
            }
        }

    }

    public class UserEntry
    {
        public string name;
        public int points;
    }
}
