using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.EventSystems;

public class SceneLoaderManager : MonoBehaviour
{
    public static SceneLoaderManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded; // Nos suscribimos al evento de carga
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    #region Scene Management

    public void LoadSceneAdditive(string sceneName)
    {
        StartCoroutine(LoadSceneAdditiveCoroutine(sceneName));
    }

    private IEnumerator LoadSceneAdditiveCoroutine(string sceneName)
    {
        if (IsSceneLoaded(sceneName))
        {
            Debug.LogWarning($"[SceneLoaderManager] La escena {sceneName} ya está cargada.");
            yield break;
        }

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

        while (!asyncLoad.isDone)
        {
            yield return null;
        }
        Debug.Log($"[SceneLoaderManager] Escena cargada aditivamente: {sceneName}");
    }

    public void UnloadScene(string sceneName)
    {
        StartCoroutine(UnloadSceneCoroutine(sceneName));
    }

    private IEnumerator UnloadSceneCoroutine(string sceneName)
    {
        if (!IsSceneLoaded(sceneName))
        {
            Debug.LogWarning($"[SceneLoaderManager] La escena {sceneName} no está cargada.");
            yield break;
        }

        AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(sceneName);

        while (!asyncUnload.isDone)
        {
            yield return null;
        }
        Debug.Log($"[SceneLoaderManager] Escena descargada: {sceneName}");
    }

    public void SwitchScene(string fromScene, string toScene)
    {
        StartCoroutine(SwitchSceneCoroutine(fromScene, toScene));
    }

    private IEnumerator SwitchSceneCoroutine(string fromScene, string toScene)
    {
        yield return StartCoroutine(LoadSceneAdditiveCoroutine(toScene));
        yield return StartCoroutine(UnloadSceneCoroutine(fromScene));
    }

    public bool IsSceneLoaded(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.name == sceneName)
            {
                return true;
            }
        }
        return false;
    }

    #endregion

    #region Canvas Management

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive)
        {
            Debug.Log($"[SceneLoaderManager] Procesando Canvas para escena: {scene.name}");
            ManageCanvases(scene);
        }
    }

    private void ManageCanvases(Scene activeScene)
    {
        Canvas[] allCanvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None); // true = incluye inactivos

        foreach (Canvas canvas in allCanvases)
        {
            if (canvas.gameObject.scene == activeScene)
            {
                canvas.gameObject.SetActive(true); // Activar Canvas de la nueva escena
            }
            else if (canvas.gameObject.scene.isLoaded && !IsInDontDestroyOnLoad(canvas.gameObject))
            {
                canvas.gameObject.SetActive(false); // Desactivar Canvas de otras escenas cargadas
            }
        }
    }

    private bool IsInDontDestroyOnLoad(GameObject obj)
    {
        return obj.scene.name == null || obj.scene.name == "DontDestroyOnLoad";
    }

    #endregion
}
