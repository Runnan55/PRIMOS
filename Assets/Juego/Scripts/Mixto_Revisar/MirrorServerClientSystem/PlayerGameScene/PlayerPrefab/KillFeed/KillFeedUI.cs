using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KillFeedUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform container;   // padre donde apilar
    [SerializeField] private KillFeedEntry entryPrefab; // tu DeathMarker

    [Header("Behavior")]
    [SerializeField] private int maxEntries = 6;
    [SerializeField] private float keepSeconds = 8f;

    private readonly List<KillFeedEntry> entries = new List<KillFeedEntry>();

    void Awake()
    {
        if (container == null) container = (RectTransform)transform;
    }

    // API simple: nombres solamente
    public void AddKillNames(string killerName, string victimName)
    {
        if (entryPrefab == null || container == null) return;

        // crear arriba (por orden de hijo)
        var e = Instantiate(entryPrefab, container);
        e.transform.SetSiblingIndex(0);
        e.Setup(killerName, victimName);
        entries.Insert(0, e);

        // limitar a maxEntries
        while (entries.Count > maxEntries)
        {
            var last = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            if (last) Destroy(last.gameObject);
        }

        // vida util
        if (keepSeconds > 0f) StartCoroutine(AutoExpire(e, keepSeconds));
    }

    private IEnumerator AutoExpire(KillFeedEntry e, float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (e == null) yield break;

        // quitar de la lista y destruir
        int idx = entries.IndexOf(e);
        if (idx >= 0) entries.RemoveAt(idx);
        if (e != null) Destroy(e.gameObject);
    }
}
