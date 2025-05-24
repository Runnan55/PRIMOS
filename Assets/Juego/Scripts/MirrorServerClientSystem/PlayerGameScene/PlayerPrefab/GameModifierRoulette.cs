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
    public Transform startPoint;
    public Transform endPoint;

    [Header("Scroll Configuración")]
    public float scrollSpeed = 500f;
    public float spawnInterval = 0.7f;

    [Header("Detención del ganador")]
    public float stopWindowThreshold = 3f;
    public float targetX = 0f;         // Centro visual donde debe detenerse
    public float triggerRange = 25f;   // Rango permitido alrededor del centro
    public float minSpinTime = 1.5f;   // Tiempo mínimo antes de permitir freno

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
    private List<GameObject> activeStones = new();
    private List<Coroutine> movingCoroutines = new();

    public void StartRoulette(GameModifierType winner, float duration)
    {
        rouletteTimer = 0f;
        winnerType = winner;
        totalDuration = duration;
        isSpinning = true;
        hasStopped = false;
        forceStopRequested = false;
        movingCoroutines.Clear();
        activeStones.Clear();

        if (gameObject.activeSelf)
            StartCoroutine(ForceStopAfter(duration));

        SetupPrefabDict();
        StartCoroutine(SpawnLoop());
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

    private IEnumerator SpawnLoop()
    {
        while (isSpinning)
        {
            GameModifierType randomType = GetNextType();
            GameObject prefab = prefabDict[randomType];
            GameObject stone = Instantiate(prefab, startPoint.position, Quaternion.identity, transform);

            // Asignar tipo
            var stoneType = stone.AddComponent<StoneType>();
            stoneType.type = randomType;

            activeStones.Add(stone);
            Coroutine moveRoutine = StartCoroutine(MoveStone(stone));
            movingCoroutines.Add(moveRoutine);

            yield return new WaitForSeconds(spawnInterval);
        }
    }

    private IEnumerator MoveStone(GameObject stone)
    {
        RectTransform rt = stone.GetComponent<RectTransform>();
        Image img = stone.GetComponent<Image>();
        float distance = Vector3.Distance(startPoint.position, endPoint.position);
        float centerX = startPoint.position.x + distance / 2f;
        float highlightRange = distance * 0.2f / 2f;

        StoneType stoneType = stone.GetComponent<StoneType>();
        if (stoneType == null) yield break;

        while (stone != null && !hasStopped && !forceStopRequested)
        {
            rouletteTimer += Time.deltaTime;
            float timeRemaining = Mathf.Max(0f, totalDuration - rouletteTimer);

            stone.transform.position += Vector3.right * scrollSpeed * Time.deltaTime;
            float stoneX = stone.transform.position.x;

            bool isInHighlightZone = Mathf.Abs(stoneX - centerX) <= highlightRange;
            float targetScale = isInHighlightZone ? highlightScale.x : normalScale.x;
            Color targetColor = isInHighlightZone ? highlightColor : normalColor;

            rt.localScale = Vector3.Lerp(rt.localScale, Vector3.one * targetScale, Time.deltaTime * 8f);
            if (img != null)
                img.color = Color.Lerp(img.color, targetColor, Time.deltaTime * 8f);

            if (!hasStopped &&
                stoneType.type == winnerType &&
                timeRemaining <= stopWindowThreshold &&
                rouletteTimer >= minSpinTime &&
                Mathf.Abs(stoneX - targetX) <= triggerRange)
            {
                Vector3 pos = stone.transform.position;
                pos.x = targetX;
                stone.transform.position = pos;

                isSpinning = false;
                hasStopped = true;
                HighlightStone(stone);

                foreach (Coroutine c in movingCoroutines)
                {
                    if (c != null) StopCoroutine(c);
                }
                movingCoroutines.Clear();

                yield break;
            }

            if (stone.transform.position.x >= endPoint.position.x)
            {
                if (!hasStopped)
                {
                    Destroy(stone);
                }
                yield break;
            }

            yield return null;
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
