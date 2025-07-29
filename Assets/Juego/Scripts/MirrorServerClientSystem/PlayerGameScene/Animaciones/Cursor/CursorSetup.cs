using UnityEngine;

public class CursorSetup : MonoBehaviour
{
    public Texture2D customCursorTexture;
    public Vector2 hotspot = Vector2.zero;

    void Start()
    {
#if UNITY_WEBGL
        Cursor.SetCursor(customCursorTexture, hotspot, CursorMode.ForceSoftware);
#else
        Cursor.SetCursor(customCursorTexture, hotspot, CursorMode.Auto);
#endif
    }
}
