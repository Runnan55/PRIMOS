using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

public class OfflineAutoReconnect : MonoBehaviour
{
    [Header("Intervals (seconds)")]
    [SerializeField] private float firstDelaySeconds = 5f;
    [SerializeField] private float retryIntervalSeconds = 5f;

    [Header("Scenes")]
    [SerializeField] private string offlineSceneName = "Offline2Scene";

    private bool isTrying;
    private float backoff;

    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        StartCoroutine(ReconnectLoop());
    }

    private IEnumerator ReconnectLoop()
    {
        yield return new WaitForSecondsRealtime(firstDelaySeconds);

        while (true)
        {
            bool inOffline = string.IsNullOrEmpty(offlineSceneName)
                             || SceneManager.GetActiveScene().name == offlineSceneName;

            if (inOffline && !NetworkClient.isConnected && !NetworkClient.active && !isTrying)
            {
                StartCoroutine(TryReloadPage());
            }

            float waitSec = retryIntervalSeconds + backoff;
            if (waitSec < 0.5f) waitSec = 0.5f;
            yield return new WaitForSecondsRealtime(waitSec);
        }
    }

    private IEnumerator TryReloadPage()
    {
        isTrying = true;
        backoff = 0f;

#if UNITY_WEBGL && !UNITY_EDITOR
    // Pide reload de misma pestaña poniendo una bandera en localStorage.
    // index.html detecta esta bandera y llama location.reload().
    try
    {
        WebGLStorage.SaveString("PRIMOS_REQUEST_RELOAD", "1");
        Debug.Log("[OfflineAutoReconnect] Requesting in-tab reload via localStorage flag...");
    }
    catch { Debug.LogWarning("[OfflineAutoReconnect] Could not set reload flag."); }
#else
        Debug.Log("[OfflineAutoReconnect] WebGL only. In editor/standalone no reload performed.");
#endif

        // Espera un poco; si seguimos aqui, aplica backoff para el siguiente intento
        yield return new WaitForSecondsRealtime(3f);

        if (!NetworkClient.isConnected)
            backoff = 3f;

        isTrying = false;
    }
}
