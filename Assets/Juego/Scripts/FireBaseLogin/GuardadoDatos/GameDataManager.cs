using System.IO;
using UnityEngine;

public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }

    public UserGameData CurrentData { get; private set; }
    private string currentUserId;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private string GetPath(string userId)
    {
        return Path.Combine(Application.persistentDataPath, $"userdata_{userId}.json");
    }

    public void LoadOrCreateData(string userId, string defaultName)
    {
        currentUserId = userId;
        string path = GetPath(userId);

        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            CurrentData = JsonUtility.FromJson<UserGameData>(json);
            Debug.Log($"[GameDataManager] Datos cargados para {userId}");
        }
        else
        {
            CurrentData = new UserGameData(defaultName);
            SaveCurrentData();
            Debug.Log($"[GameDataManager] Datos nuevos creados para {userId}");
        }
    }

    public void SaveCurrentData()
    {
        if (CurrentData == null || string.IsNullOrEmpty(currentUserId)) return;

        string json = JsonUtility.ToJson(CurrentData, true);
        File.WriteAllText(GetPath(currentUserId), json);
        Debug.Log($"[GameDataManager] Datos guardados para {currentUserId}");
    }

    public bool HasData => CurrentData != null;
}