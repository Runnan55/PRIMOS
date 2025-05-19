using System.Runtime.InteropServices;
using UnityEngine;

public static class WebGLStorage
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void SaveToLocalStorage(string key, string value);

    [DllImport("__Internal")]
    private static extern System.IntPtr LoadFromLocalStorage(string key);

    [DllImport("__Internal")]
    private static extern void RemoveFromLocalStorage(string key);

    public static void SaveString(string key, string value)
    {
        SaveToLocalStorage(key, value);
    }

    public static string LoadString(string key)
    {
        var ptr = LoadFromLocalStorage(key);
        return ptr == System.IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
    }

    public static void DeleteKey(string key)
    {
        RemoveFromLocalStorage(key);
    }
#else
    public static void SaveString(string key, string value)
    {
        PlayerPrefs.SetString(key, value);
    }

    public static string LoadString(string key)
    {
        return PlayerPrefs.GetString(key, null);
    }

    public static void DeleteKey(string key)
    {
        PlayerPrefs.DeleteKey(key);
    }
#endif
}