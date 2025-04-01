using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LeaderboardUI : MonoBehaviour
{
    public GameObject leaderboardEntryPrefab;
    public Transform leaderboardContent;
    public GameObject leaderboardCanvas;

    private List<string> receivedStats = new List<string>();

    public void AddLeaderboardEntry(string statsLine)
    {
        receivedStats.Add(statsLine); // Almacenar las líneas recibidas
    }

    public void DisplayLeaderboard()
    {
        // Limpiar entradas anteriores
        foreach (Transform child in leaderboardContent)
        {
            Destroy(child.gameObject);
        }

        foreach (string entry in receivedStats)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;

            string[] data = entry.Split(',');

            if (data.Length != 6)
            {
                Debug.LogError("Formato de estadística incorrecto.");
                continue;
            }

            string playerName = data[0];
            int kills = int.Parse(data[1]);
            int bulletsReloaded = int.Parse(data[2]);
            int bulletsFired = int.Parse(data[3]);
            int damageDealt = int.Parse(data[4]);
            int timesCovered = int.Parse(data[5]);

            GameObject entryObject = Instantiate(leaderboardEntryPrefab, leaderboardContent);
            Text[] texts = entryObject.GetComponentsInChildren<Text>();

            texts[0].text = playerName;
            texts[1].text = $"Kills: {kills}";
            texts[2].text = $"Balas Recargadas: {bulletsReloaded}";
            texts[3].text = $"Balas Disparadas: {bulletsFired}";
            texts[4].text = $"Daño Infligido: {damageDealt}";
            texts[5].text = $"Veces Cubierto: {timesCovered}";
        }

        receivedStats.Clear(); // Limpiar la lista para futuros usos
        leaderboardCanvas.SetActive(true);
    }
}
