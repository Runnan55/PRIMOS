using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.UI;

public class GameModifierRoulette : MonoBehaviour
{
    [Header("Opciones de Ruleta")]
    public List<GameObject> modifierStones;
    public float minDuration = 5f;
    public float maxDuration = 10f;
    public float intervalStart = 0.15f;
    public float intervalEnd = 0.75f;

    [Header("Efectos Visuales")]
    public Color normalColor = Color.gray;
    public Color highlightColor = Color.white;
    public Vector3 normalScale = Vector3.one * 0.75f;
    public Vector3 highlightScale = Vector3.one;

    public int selectedIndex { get; private set; } = -1;

    public System.Action<int> OnModifierSelected; //Callback cuando finaliza

    public void StartRoulette()
    {
        StartCoroutine(RouletteRoutine());
    }

    private IEnumerator RouletteRoutine()
    {
        float duration = Random.Range(minDuration, maxDuration);
        float elapsed = 0f;

        int currentIndex = 0;
        float t = 0;

        while (elapsed < duration)
        {
            Highlight(currentIndex);
            float progress = elapsed / duration;
            float interval = Mathf.Lerp(intervalStart, intervalEnd, progress);

            yield return new WaitForSeconds(interval);

            elapsed += interval;
            currentIndex = (currentIndex + 1) % modifierStones.Count;
        }

        selectedIndex = currentIndex;
        Highlight(selectedIndex);

        Debug.Log($"[Ruleta] Modificador seleccionado: {modifierStones[selectedIndex].name}");

        OnModifierSelected?.Invoke(selectedIndex);
    }

    private void Highlight(int index)
    {
        for (int i = 0; i < modifierStones.Count; i++)
        {
            var image = modifierStones[i].GetComponent<Image>();
            modifierStones[i].transform.localScale = (i == index) ? highlightScale : normalScale;

            if (image != null)
                image.color = (i == index) ? highlightColor : normalColor;
        }
    }
}
