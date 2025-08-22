using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoaderManager : MonoBehaviour
{
    public static SceneLoaderManager Instance { get; private set; }

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

    public void LoadScene(string sceneName)
    {
        if (SceneManager.GetActiveScene().name == sceneName)
        {
            Debug.LogWarning($"[SceneLoaderManager] Ya estamos en la escena {sceneName}.");
            return;
        }

        Debug.Log($"[SceneLoaderManager] Cargando escena {sceneName} en modo Single.");
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }
}
