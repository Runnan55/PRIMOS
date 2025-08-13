using UnityEngine;

public class CursorSetup : MonoBehaviour
{
    public static CursorSetup I { get; private set; }

    [Header("Cursors")]
    public Texture2D pinkCursor;
    public Vector2 pinkHotspot = Vector2.zero;

    public Texture2D crosshairCursor;
    public Vector2 crosshairHotspot;

    private void Awake()
    {
        crosshairHotspot = new Vector2(crosshairCursor.width / 2f, crosshairCursor.height / 2f);
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        UsePinkCursor();
    }

    public void UsePinkCursor()
    {
#if UNITY_WEBGL
        Cursor.SetCursor(pinkCursor, pinkHotspot, CursorMode.ForceSoftware);
#else
        Cursor.SetCursor(pinkCursor, pinkHotspot, CursorMode.Auto);
#endif
        Cursor.visible = true;
    }

    public void UseCrosshairCursor()
    {
#if UNITY_WEBGL
        Cursor.SetCursor(crosshairCursor, crosshairHotspot, CursorMode.ForceSoftware);
#else
        Cursor.SetCursor(crosshairCursor, crosshairHotspot, CursorMode.Auto);
#endif
        Cursor.visible = true;
    }
}
