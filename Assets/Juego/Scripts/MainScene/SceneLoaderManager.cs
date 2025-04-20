using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.EventSystems;

public class SceneLoaderManager : MonoBehaviour
{
    public static SceneLoaderManager Instance {  get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        Instance = this;
    }

    //Cargar una escena aditivamente
    public void LoadSceneAdditive(string sceneName)
    {
        StartCoroutine(LoadSceneAdditiveCoroutine(sceneName));
    }

    private IEnumerator LoadSceneAdditiveCoroutine(string sceneName)
    {
        if (IsSceneLoaded(sceneName))
        {
            Debug.LogWarning($"Scene {sceneName} is already loaded.");
            yield break;
        }

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

        while (!asyncLoad.isDone)
        {
            yield return null;
        }
        Debug.Log($"Scene {sceneName} loaded additively.");
    }
        
    //Descargar una escena
    public void UnloadScene(string sceneName)
    {
        StartCoroutine(UnloadSceneCoroutine(sceneName));
    }

    private IEnumerator UnloadSceneCoroutine(string sceneName)
    {
        if (!IsSceneLoaded(sceneName))
        {
            Debug.LogWarning($"Scene {sceneName} is not loaded.");
            yield break;
        }

        AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(sceneName);

        while (!asyncUnload.isDone)
        {
            yield return null;
        }
        Debug.Log($"Scene {sceneName} unloaded.");
    }

    //Cambiar de una escena aditiva a otra
    public void SwitchScene(string fromScene, string toScene)
    {
        StartCoroutine(SwitchSceneCoroutine(fromScene, toScene));
    }

    private IEnumerator SwitchSceneCoroutine(string fromScene, string toScene)
    {
        yield return StartCoroutine(LoadSceneAdditiveCoroutine(toScene));
        yield return StartCoroutine(UnloadSceneCoroutine(fromScene));
    }

    //Verificar si una escena ya está cargada
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

}
