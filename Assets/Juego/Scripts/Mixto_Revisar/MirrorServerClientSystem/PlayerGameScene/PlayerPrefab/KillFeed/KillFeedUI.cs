using System.Collections.Generic;
using UnityEngine;

public class KillFeedUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform container;   // padre donde apilar
    [SerializeField] private KillFeedEntry entryPrefab; // tu DeathMarker

    [Header("Behavior")]
    [SerializeField] private int maxEntries = 6;

    private readonly List<KillFeedEntry> entries = new List<KillFeedEntry>();

    void Awake()
    {
        if (container == null) container = (RectTransform)transform;
    }

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
    }

    public void ClearAll()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i]) Destroy(entries[i].gameObject);
        }
        entries.Clear();
    }
}
