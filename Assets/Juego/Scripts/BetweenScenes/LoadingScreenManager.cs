using UnityEngine;

public class LoadingScreenManager : MonoBehaviour
{
    public GameObject loadingCanvas;

    void Start()
    {
        loadingCanvas.SetActive(true);
    }

    public void HideLoading()
    {
        if (loadingCanvas != null)
        {
            loadingCanvas.SetActive(false);
        }
    }
}
