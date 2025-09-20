using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class ClientNotificationUI : MonoBehaviour
{
    [System.Serializable]
    public class SpriteEntry
    {
        public string key;      // e.g. "welcome", "key", "warning"
        public Sprite sprite;   // icon for that key
    }

    [Header("Refs")]
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Animator animator;

    [Header("Config")]
    [SerializeField] private float defaultDuration = 5.0f;
    [SerializeField] private Sprite defaultSprite;        // optional fallback
    [SerializeField] private List<SpriteEntry> spriteTable = new List<SpriteEntry>();

    private readonly Dictionary<string, Sprite> _dict = new Dictionary<string, Sprite>();
    private Coroutine running;

    private void Awake()
    {
        _dict.Clear();
        foreach (var e in spriteTable)
        {
            if (e != null && !string.IsNullOrWhiteSpace(e.key) && e.sprite != null)
            {
                _dict[e.key] = e.sprite;
            }
        }
    }

    // Old API (direct sprite)
    public void Show(Sprite s, string text, float duration = -1f)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(ShowRoutine(s, text, duration < 0 ? defaultDuration : duration));
    }

    // New API (by key)
    public void ShowByKey(string key, string text, float duration = -1f)
    {
        Sprite s = null;
        if (!string.IsNullOrWhiteSpace(key) && _dict.TryGetValue(key, out var found)) s = found;
        if (s == null) s = defaultSprite;

        Show(s, text, duration);
    }

    private IEnumerator ShowRoutine(Sprite s, string text, float duration)
    {
        if (icon != null) icon.sprite = s;
        if (label != null) label.text = text ?? string.Empty;

        if (animator != null) animator.Play("Entry", 0, 0f);
        yield return new WaitForSecondsRealtime(duration);
        if (animator != null) animator.Play("Exit", 0, 0f);

        running = null;
    }
}