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
    public float scrollSpeed = 500f;
    public float spawnDistance = 600f;

    [Header("Detención del ganador")]
    public float minSpinTime = 2f;

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
    private RectTransform lastSpawnedStone = null;
    private RectTransform winnerStone = null;

    public void StartRoulette(GameModifierType winner, float duration)
    {
        rouletteTimer = 0f;
        winnerType = winner;
        totalDuration = duration;
        isSpinning = true;
        hasStopped = false;
        forceStopRequested = false;

        activeStones.Clear();
        activeStoneTypes.Clear();
        lastSpawnedStone = null;
        winnerStone = null;

        if (gameObject.activeSelf)
            StartCoroutine(ForceStopAfter(duration));

        SetupPrefabDict();
        SpawnStone(); // primera piedra
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

        for (int i = 0; i < activeStones.Count; i++)
        {
            RectTransform rt = activeStones[i];
            StoneType stoneType = activeStoneTypes[i];

            Vector2 anchoredPos = rt.anchoredPosition;
            float prevX = anchoredPos.x;

            anchoredPos.x += scrollSpeed * Time.deltaTime;
            rt.anchoredPosition = anchoredPos;

            // Highlight visual
            bool isInHighlightZone = Mathf.Abs(anchoredPos.x) <= 150f;
            float targetScale = isInHighlightZone ? highlightScale.x : normalScale.x;
            Color targetColor = isInHighlightZone ? highlightColor : normalColor;

            rt.localScale = Vector3.Lerp(rt.localScale, Vector3.one * targetScale, Time.deltaTime * 8f);
            if (rt.TryGetComponent<Image>(out var img))
                img.color = Color.Lerp(img.color, targetColor, Time.deltaTime * 8f);

            // DETENER solo en la piedra ganadora marcada
            bool cruzoCentro = prevX < 0f && anchoredPos.x >= 0f;

            if (!hasStopped &&
                rt == winnerStone &&
                rouletteTimer >= minSpinTime &&
                cruzoCentro)
            {
                anchoredPos.x = 0f;
                rt.anchoredPosition = anchoredPos;

                HighlightStone(rt.gameObject);
                isSpinning = false;
                hasStopped = true;
            }

            // Destruir si se sale de pantalla
            if (anchoredPos.x >= endPoint.anchoredPosition.x)
            {
                Destroy(rt.gameObject);
                activeStones.RemoveAt(i);
                activeStoneTypes.RemoveAt(i);
                i--;
            }
        }

        // SPAWN: solo cuando la última piedra se haya alejado spawnDistance del startPoint
        if (!hasStopped && (
            lastSpawnedStone == null ||
            Mathf.Abs(lastSpawnedStone.anchoredPosition.x - startPoint.anchoredPosition.x) >= spawnDistance))
        {
            SpawnStone();
        }
    }

    private void SpawnStone()
    {
        GameModifierType randomType = GetNextType();
        GameObject prefab = prefabDict[randomType];
        GameObject stone = Instantiate(prefab, startPoint.position, Quaternion.identity, transform);

        var rt = stone.GetComponent<RectTransform>();
        var stoneType = stone.AddComponent<StoneType>();
        stoneType.type = randomType;

        if (rt != null)
        {
            rt.anchoredPosition = startPoint.anchoredPosition;

            lastSpawnedStone = rt;
            activeStones.Add(rt);
            activeStoneTypes.Add(stoneType);

            if (stoneType.type == winnerType && winnerStone == null)
            {
                winnerStone = rt;
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
