
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

public class StoneType : MonoBehaviour
{
    public GameModifierType type;
}

public class GameModifierRoulette : MonoBehaviour
{
    [Header("Prefabs por Modificador")]
    public GameObject prefabCaceria;
    public GameObject prefabGatillo;
    public GameObject prefabBalas;
    public GameObject prefabOscura;

    [Header("Puntos de Disparo")]
    public RectTransform startPoint;
    public RectTransform endPoint;

    [Header("Scroll Configuración")]
    public float scrollSpeed;
    public float spawnDistance;

    [Header("Detención del ganador")]
    public float minSpinTime;

    [Header("Efectos Visuales")]
    public Color normalColor = Color.gray;
    public Color highlightColor = Color.white;
    public Vector3 normalScale = Vector3.one * 0.75f;
    public Vector3 highlightScale = Vector3.one;

    private Dictionary<GameModifierType, GameObject> prefabDict;
    private bool isSpinning = false;
    private bool hasStopped = false;
    private float rouletteTimer = 0f;
    private GameModifierType winnerType;
    private float totalDuration;
    private bool forceStopRequested = false;

    private List<RectTransform> activeStones = new();
    private List<StoneType> activeStoneTypes = new();
    private List<RectTransform> winnerCandidates = new();
    private RectTransform winnerStone = null;

    private float scrollOffset = 0f;
    private List<StoneType> allStones = new();
    private int totalVisibleStones = 100;

    public void StartRoulette(GameModifierType winner, float duration)
    {
        winnerType = winner;
        totalDuration = duration;
        isSpinning = true;
        hasStopped = false;
        forceStopRequested = false;
        scrollOffset = 0f;

        allStones.Clear();
        activeStones.Clear();
        activeStoneTypes.Clear();
        winnerCandidates.Clear();
        winnerStone = null;

        SetupPrefabDict();

        float baseX = startPoint.anchoredPosition.x;

        for (int i = 0; i < totalVisibleStones; i++)
        {
            var type = GetNextType();
            GameObject prefab = prefabDict[type];
            GameObject stone = Instantiate(prefab, transform);

            RectTransform rt = stone.GetComponent<RectTransform>();
            var st = stone.AddComponent<StoneType>();
            st.type = type;

            float offsetX = -spawnDistance * (totalVisibleStones - 1 - i);
            rt.anchoredPosition = new Vector2(startPoint.anchoredPosition.x + offsetX, startPoint.anchoredPosition.y);

            activeStones.Add(rt);
            activeStoneTypes.Add(st);
            allStones.Add(st);

            if (type == winnerType)
            {
                winnerCandidates.Add(rt);
                if (winnerStone == null)
                    winnerStone = rt;
            }
        }

        Debug.Log($"[Roulette] Ganador elegido: {winnerType}, bloques candidatos: {winnerCandidates.Count}");
        StartCoroutine(ForceStopAfter(duration));
    }

    private void SetupPrefabDict()
    {
        prefabDict = new Dictionary<GameModifierType, GameObject>
        {
            { GameModifierType.CaceriaDelLider, prefabCaceria },
            { GameModifierType.GatilloFacil, prefabGatillo },
            { GameModifierType.BalasOxidadas, prefabBalas },
            { GameModifierType.CargaOscura, prefabOscura }
        };
    }

    private void Update()
    {
        if (!isSpinning || hasStopped || forceStopRequested) return;

        rouletteTimer += Time.deltaTime;
        scrollOffset += scrollSpeed * Time.deltaTime;

        for (int i = 0; i < activeStones.Count; i++)
        {
            var rt = activeStones[i];
            float baseX = startPoint.anchoredPosition.x - spawnDistance * (totalVisibleStones - 1 - i);
            float newX = baseX + scrollOffset;
            rt.anchoredPosition = new Vector2(newX, rt.anchoredPosition.y);

            bool isInHighlightZone = Mathf.Abs(newX) <= 150f;
            float targetScale = isInHighlightZone ? highlightScale.x : normalScale.x;
            Color targetColor = isInHighlightZone ? highlightColor : normalColor;

            rt.localScale = Vector3.Lerp(rt.localScale, Vector3.one * targetScale, Time.deltaTime * 8f);
            if (rt.TryGetComponent<Image>(out var img))
                img.color = Color.Lerp(img.color, targetColor, Time.deltaTime * 8f);

            bool cruzoCentro = (baseX + (scrollOffset - scrollSpeed * Time.deltaTime)) < 0f && newX >= 0f;

            if (!hasStopped &&
                winnerCandidates.Contains(rt) &&
                rouletteTimer >= (totalDuration - minSpinTime) &&
                cruzoCentro)
            {
                isSpinning = false;
                hasStopped = true;

                HighlightStone(rt.gameObject);

                foreach (var other in winnerCandidates)
                {
                    if (other != rt && other.TryGetComponent<Image>(out var img2))
                    {
                        img2.color = normalColor;
                        other.localScale = normalScale;
                    }
                }

                AudioManager.Instance.PlaySFX("GlobalMission_Selected");
                AudioManager.Instance.musicSource.Stop();
                Debug.Log("[Roulette] Ruleta se detuvo por cruce central.");
            }
        }
    }

    private void HighlightStone(GameObject stone)
    {
        if (stone.TryGetComponent<Image>(out var img))
            img.color = highlightColor;

        stone.transform.localScale = highlightScale;
    }

    private IEnumerator ForceStopAfter(float wait)
    {
        yield return new WaitForSeconds(wait);

        if (!hasStopped)
        {
            forceStopRequested = true;
            isSpinning = false;
            hasStopped = true;

            if (winnerStone != null)
            {
                HighlightStone(winnerStone.gameObject);
                EnableChildParticles(winnerStone.gameObject);
            }
        }
    }

    private void EnableChildParticles(GameObject root)
    {
        // generic: find any ParticleSystem in children (even inactive)
        var ps = root.GetComponentInChildren<ParticleSystem>(true);
        if (ps != null)
        {
            ps.gameObject.SetActive(true);
        }
    }

    private int currentIndex = 0;

    private GameModifierType GetNextType()
    {
        var values = new List<GameModifierType>(prefabDict.Keys);
        GameModifierType nextType = values[currentIndex];
        currentIndex = (currentIndex + 1) % values.Count;
        return nextType;
    }
}
