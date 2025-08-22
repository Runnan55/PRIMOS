using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using TMPro;
using System.Linq;
using System;
using Mirror.BouncyCastle.Security;
using Unity.VisualScripting;
using UnityEngine.SceneManagement;

public enum GameModifierType
{
    //DobleAgente,
    CaceriaDelLider,
    GatilloFacil,
    BalasOxidadas,
    //BendicionDelArsenal,
    CargaOscura
}

public enum QuickMissionType
{
    DealDamage, //Inflinge daño
    BlockShot,  //Bloquea un disparo
    ReloadAndTakeDamage, //Recarga y recibe daño
    DoNothing //No hagas nada
}

public class QuickMission
{
    public QuickMissionType type;
    public int assignedRound;
    public bool completed = false;

    public QuickMission(QuickMissionType type, int assignedRound)
    {
        this.type = type;
        this.assignedRound = assignedRound;
    }
}

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }
    [SerializeField] private bool allowAccumulatedDamage = false; //Estilo de juego con daño acumulable o solo 1 de daño por turno

    [Header("Rondas")]
    private float currentDecisionTime;
    [SerializeField] private float decisionTime; // Tiempo para elegir acción
    [SerializeField] private float executionTime = 4f; // Tiempo para mostrar resultados
    //[SerializeField] private TMP_Text timerText;

    public string mode;

    private bool isDecisionPhase = true;
    private bool isGameOver = false;
    private Coroutine roundCycleCoroutine;
    private Coroutine decisionPhaseCoroutine;
    private Coroutine executionPhaseCoroutine;

    [SerializeField] public List<PlayerController> players = new List<PlayerController>(); //Lista de jugadores que entran
    [SerializeField] private List<PlayerController> deadPlayers = new List<PlayerController>(); //Lista de jugadores muertos
    private HashSet<PlayerController> damagedPlayers = new HashSet<PlayerController>(); // Para almacenar jugadores que ya recibieron daño en la ronda
    private HashSet<string> startingHumans = new HashSet<string>(); // SnapShot de humanos que inician el juego
    private HashSet<uint> startingNetIds = new HashSet<uint>(); // Snapshot de TODOS los que inician (humanos y bots)


    private Dictionary<PlayerController, PlayerAction> actionsQueue = new Dictionary<PlayerController, PlayerAction>();
    private Dictionary<string, string> playerIdToUid = new Dictionary<string, string>();

    private int currentRound = 0; // Contador de rondas
                                  //[SerializeField] private TMP_Text roundText; // Texto en UI para mostrar la ronda

    [Header("LoadingScreen")]
    private LoadingScreenManager loadingScreen;

    [Header("StartEndDraw")]
    private bool isDraw = false;
    private bool isGameStarted = false;

    [Header("MisionesDeInicio")]
    [SerializeField] public GameModifierType SelectedModifier;
    [SerializeField] public List<PlayerController> veryHealthy = new List<PlayerController>(); //Guardar jugadores con más vida
    [SerializeField] private bool randomizeModifier = false;

    [Header("MisionesRápidas")]
    [SerializeField] private List<int> missionRounds = new List<int> { }; //Rondas donde se activan misiones
    [SerializeField] private int missionsPerRound = 1; //Cantidad de jugadores que reciben la mision por ronda

    private Queue<PlayerController> missionQueue = new Queue<PlayerController>();
    private HashSet<PlayerController> playersWhoHadMission = new HashSet<PlayerController>();

    [Header("GameStatistics")]
    [SerializeField] private GameStatistic gameStatistic;
    private int deathCounter = 0;

    [Header("Talisman-Tiki")]
    [SerializeField] private GameObject talismanIconPrefab;

    private PlayerController talismanHolder;
    [SyncVar] private uint talismanHolderNetId;
    private List<PlayerController> tikiHistory = new List<PlayerController>();

    [Header("MathHandler")]
    [SyncVar] public string matchId; //Identificador de partida
    public GameObject playerControllerPrefab; //Prefab asignado por MatchHandler

    [Header("Bot Bot Bot")]
    private Dictionary<PlayerController, Queue<PlayerController>> recentAttackers = new();

    [Header("Orden de muerte para el Leaderboard")]
    private List<PlayerController> deathBuffer = new();
    private bool isProcessingDeath = false;

    private void IdentifyVeryHealthy()
    {
        try
        {
            if (players.Count == 0 || SelectedModifier != GameModifierType.CaceriaDelLider) return;

            int maxHealth = players.Max(player => player.health); //Buscar la mayor cantidad de vida

            veryHealthy = players.Where(player => player.health == maxHealth).ToList(); //Filtrar los que tienen vida maxima

            foreach (var player in players)
            {
                bool shouldBeHealthy = veryHealthy.Contains(player);
                player.isVeryHealthy = shouldBeHealthy;
            }
        }
        catch (Exception e) { Debug.LogError($"[GM] IdentifyVeryHealthy() fallo: {e}"); }
    }

    void OnDisable()
    {
        Debug.LogWarning("GameManager ha sido desactivado!", this);
    }

    private void Start()
    {
        Debug.Log("GameManager activo en Start: " + gameObject.activeSelf, this);

        if (isServer)
        {
            if (mode == "Ranked")
            {
                decisionTime = 5f;
            }
            else
            {
                decisionTime = 10f;
            }

            talismanHolder = players.FirstOrDefault(p => p.isAlive);
            if (talismanHolder != null)
            {
                talismanHolderNetId = talismanHolder.netId;
                //RpcSpawnTalisman(talismanHolderNetId);
                UpdateTikiVisual(talismanHolder);
            }
        }
    }

    #region Invoke Players

    public override void OnStartServer()
    {
        if (!isServer) return; // Solo servidor ejecuta esto

        base.OnStartServer();

        Debug.Log("[GameManager] Iniciado en servidor.");

        readyTimeoutCoroutine = StartCoroutine(WaitPlayersOrAbort());
    }

    [Server]
    public void OnPlayerSceneReady(CustomRoomPlayer roomPlayer)
    {
        if (players.Any(p => p.connectionToClient == roomPlayer.connectionToClient))
        {
            Debug.Log($"[GameManager] El jugador {roomPlayer.playerName} ya tiene un PlayerController instanciado.");
            return;
        }

        // Obtener spawn positions disponibles
        List<Vector3> spawnPositions = gameObject.scene.GetRootGameObjects()
            .SelectMany(go => go.GetComponentsInChildren<NetworkStartPosition>())
            .Select(pos => pos.transform.position)
            .ToList();

        // Obtener ya los PlayerControllers vivos
        List<PlayerController> existingPlayers = players.Where(p => p.isAlive).ToList();

        // Eliminar posiciones ya ocupadas
        foreach (var player in existingPlayers)
        {
            Vector3 playerPos = player.transform.position;

            // Elimina posiciones que ya están ocupadas (muy cerca)
            spawnPositions.RemoveAll(pos => Vector3.Distance(pos, playerPos) < 0.5f);
        }

        // Elegir una aleatoria de las que quedaron
        Vector3 spawnPos = spawnPositions.Count > 0
            ? spawnPositions[UnityEngine.Random.Range(0, spawnPositions.Count)]
            : Vector3.zero;

        GameObject playerInstance = Instantiate(playerControllerPrefab, spawnPos, Quaternion.identity);
        NetworkServer.Spawn(playerInstance, roomPlayer.connectionToClient);
        SceneManager.MoveGameObjectToScene(playerInstance, gameObject.scene);

        PlayerController controller = playerInstance.GetComponent<PlayerController>();
        controller.gameManagerNetId = netId; // <- Vincula de inmediato al playerPrefab con el GameManager, por alguna razón sin esto revienta y el cliente no detecta GManager del server
        controller.playerName = roomPlayer.playerName;
        controller.playerId = roomPlayer.playerId;
        controller.ownerRoomPlayer = roomPlayer;
        controller.firebaseUID = roomPlayer.firebaseUID;

        roomPlayer.linkedPlayerController = controller;

        RegisterPlayer(controller);
    }

    [Server]
    public void RegisterPlayer(PlayerController player)
    {
        if (!players.Contains(player))
        {
            players.Add(player);

            // Fallback de UID por si el ownerRoomPlayer desaparece más tarde
            string uid = player.firebaseUID;
            if (string.IsNullOrEmpty(uid) && player.ownerRoomPlayer != null)
                uid = player.ownerRoomPlayer.firebaseUID;

            if (!string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(player.playerId))
                playerIdToUid[player.playerId] = uid;

            // Verificar si podemos empezar la partida
            CheckAllPlayersReady();
        }
    }

    #endregion

    private void ChooseRandomModifier()
    {
        Array values = Enum.GetValues(typeof(GameModifierType));

        SelectedModifier = (GameModifierType)values.GetValue(UnityEngine.Random.Range(0, values.Length));
    }

    private void ApplyGameModifier(GameModifierType modifier)
    {
        switch (modifier)
        {
            /*case GameModifierType.DobleAgente:
                foreach (var player in players) player.RpcPlayAnimation("GM_DobleAgente");
                break;*/
            case GameModifierType.CaceriaDelLider:
                foreach (var player in players) player.RpcPlayAnimation("GM_CaceriaDelLider");
                //Funciona pero en el inspector hay que seleccionar
                break;
            case GameModifierType.GatilloFacil:
                foreach (var player in players)
                {
                    player.RpcPlayAnimation("GM_GatilloFacil");
                    player.ServerReload(); // Otorgar munición extra
                }
                break; 
            case GameModifierType.BalasOxidadas:
                foreach (var player in players)
                {
                    player.RpcPlayAnimation("GM_BalasOxidadas");
                    player.rustyBulletsActive = true;
                }
                 break;
             /*case GameModifierType.BendicionDelArsenal:
                 foreach (var player in players) player.RpcPlayAnimation("GM_BendicionDelArsenal");
                 break;*/
            case GameModifierType.CargaOscura:
                foreach (var player in players)
                {
                    player.RpcPlayAnimation("GM_CargaOscura");
                    player.isDarkReloadEnabled = true;
                }
                break;
            default:
                break;
        }
    }

    public void CheckAllPlayersReady()
    {
        if (!isServer || isGameStarted) return;

        MatchInfo match = MatchHandler.Instance.GetMatch(matchId);
        if (match == null) return;

        if (match.players.Count != players.Count)
        {
            Debug.Log($"[GameManager] Esperando... esperados: {match.players.Count}, instanciados: {players.Count}");
            return;
        }

        // ⬇️ NUEVO: cancelar el timeout si seguía corriendo
        if (readyTimeoutCoroutine != null) { StopCoroutine(readyTimeoutCoroutine); readyTimeoutCoroutine = null; }

        // Snapshot de humanos que empiezan
        startingHumans = new HashSet<string>(
            MatchHandler.Instance.GetMatch(matchId).players
                .Where(p => !string.IsNullOrEmpty(p.firebaseUID))
                .Select(p => p.firebaseUID)
        );

        isGameStarted = true;
        FillBotsIfNeeded();

        // Puntuar solo a los que estaban al inicio del juego, si se van antes de empezar no suman ni restan RP
        startingHumans = new HashSet<string>(
            players.Where(p => !p.isBot && !string.IsNullOrEmpty(p.firebaseUID))
           .Select(p => p.firebaseUID)
            );

        startingNetIds = new HashSet<uint>(players.Select(p => p.netId));

        StartCoroutine(BegingameAfterDelay());
    }

    private Coroutine readyTimeoutCoroutine;
    [SerializeField] private float readyTimeoutSeconds = 15f;


    [Server]
    private IEnumerator WaitPlayersOrAbort()
    {
        float deadline = Time.realtimeSinceStartup + readyTimeoutSeconds;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (isGameStarted) yield break;

            var match = MatchHandler.Instance.GetMatch(matchId);
            if (match == null) yield break;

            // ¿ya están todos instanciados?
            if (players.Count >= match.players.Count)
            {
                CheckAllPlayersReady();   // <- arranca aunque la igualdad se logre porque alguien se fue
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }

        MatchHandler.Instance.AbortMatch(matchId, "timeout_waiting_players");

        if (readyTimeoutCoroutine != null) { StopCoroutine(readyTimeoutCoroutine); readyTimeoutCoroutine = null; }
    }



    #region BOT BOT BOT

    [Server]
    private void FillBotsIfNeeded()
    {
        MatchInfo match = MatchHandler.Instance.GetMatch(matchId);
        if (match == null) return;

        // Calcular bots según humanos esperados (no instanciados)
        int humanExpected = match.players.Count;                              // humanos asignados a la sala
        int currentBots = players.Count(p => p.isBot);                        // bots ya creados (por seguridad)
        int needed = MatchHandler.MATCH_SIZE - humanExpected - currentBots;
        if (needed <= 0) return;

        List<Vector3> spawnPositions = gameObject.scene.GetRootGameObjects()
            .SelectMany(go => go.GetComponentsInChildren<NetworkStartPosition>())
            .Select(pos => pos.transform.position).ToList();

        for (int i = 0; i < needed; i++)
        {
            // Recalcula las posiciones disponibles en cada iteración
            List<Vector3> availablePositions = spawnPositions
                .Where(pos => players.All(p => Vector3.Distance(p.transform.position, pos) >= 0.5f))
                .ToList();

            Vector3 pos = availablePositions.Count > 0
                ? availablePositions[UnityEngine.Random.Range(0, availablePositions.Count)]
                : Vector3.zero;

            GameObject bot = Instantiate(playerControllerPrefab, pos, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(bot, gameObject.scene);
            NetworkServer.Spawn(bot);

            PlayerController pc = bot.GetComponent<PlayerController>();
            pc.playerName = NameGenerator();
            pc.isBot = true;
            pc.gameManagerNetId = netId;
            pc.botPersonality = (BotPersonality)UnityEngine.Random.Range(0, Enum.GetValues(typeof(BotPersonality)).Length);
            RegisterPlayer(pc);
        }
    }

    #region NameGenerator

    private string[] prefixes = {
    "Zan", "Kor", "Vel", "Thar", "Lum", "Nex", "Mal", "Run", "Luc", "Put",
    "Vor", "Xel", "Dro", "Gar", "Kel", "Mor", "Sar", "Tor", "Val", "Yor",
    "Zer", "Alk", "Bren", "Cyn", "Del", "Ekr", "Fal", "Gor", "Hul", "Irn",
    "Aza"
    };

    private string[] suffixes = {
    "trik", "vel", "dor", "gorn", "ion", "rax", "mir", "nan", "ius", "in",
    "gath", "nor", "zoth", "arn", "vex", "mon", "zul", "grim", "ros", "ther",
    "vek", "lorn", "drix", "zor", "thus", "kan", "jorn", "mok", "thar", "quinn",
    "thot"
    };

    private string[] fullNames = {
        "Cazaputas42", "Jajaja", "Jejeje", "Jijiji", "DonComedia", "MataPanchos", "ChamucoReload",
        "CasiTeDoy", "TukiReload", "XDxdxDxd", "lolarion", "Terreneitor", "TengoHambre", "pichulaTriste",
        "CryBaby", "WannaCry", "RobertScranton"
    };

    public string NameGenerator()
    {
        float roll = UnityEngine.Random.value;

        //10% de probabilidad de usar un nombre completo
        if (roll <= 0.1f)
        {
            return fullNames[UnityEngine.Random.Range(0, fullNames.Length)];
        }

        string prefix = prefixes[UnityEngine.Random.Range(0, prefixes.Length)];
        string suffix = suffixes[UnityEngine.Random.Range(0, suffixes.Length)];
        return prefix + suffix;
    }

    #endregion

    #endregion

    [Server]
    private IEnumerator BegingameAfterDelay()
    {
        if (gameStatistic != null)
        {
            gameStatistic.Initialize(players);
        }

        foreach (var p in players)
        {
            p.RpcHideLoadingScreen();
        }

        if (talismanHolder != null)
        {
            talismanHolderNetId = talismanHolder.netId;

            // Simular una ronda previa de Tiki para establecer prioridad de disparo desde el inicio
            tikiHistory.Clear();

            int startIndex = players.IndexOf(talismanHolder);
            int count = players.Count;

            for (int i = 1; i < count; i++) // No incluimos al actual poseedor todavía
            {
                int index = (startIndex + i) % count; // Avanza en sentido horario
                tikiHistory.Add(players[index]);
            }

            tikiHistory.Add(talismanHolder); // El actual poseedor va al final (último en tenerlo)
        }

        if (randomizeModifier)
        {
            yield return StartCoroutine(ShowRouletteBeforeStart());

            // Failsafe #1: fuerza cerrar la ruleta en todos los clientes por si algún cliente aún estaba resync
            foreach (var p in players)
                if (!p.isBot && p.connectionToClient != null)
                    p.TargetHideRouletteCanvas(p.connectionToClient);
        }

        ApplyGameModifier(SelectedModifier);

        roundCycleCoroutine = StartCoroutine(RoundCycle());

        talismanHolder = players.FirstOrDefault(p => p.isAlive); // El primero con vida

        if (loadingScreen != null)
        {
            loadingScreen.HideLoading();
        }
    }

    #region AccumulatedDamageY/N
    public bool AllowAccumulatedDamage()
    {
        return allowAccumulatedDamage;
    }

    [Server]
    public bool HasTakenDamage(PlayerController player)
    {
        return damagedPlayers.Contains(player);
    }

    [Server]
    public void RegisterDamagedPlayer(PlayerController player)
    {
        damagedPlayers.Add(player);
    }

    #endregion

    #region WatchDog

    // Ejecuta una coroutine con timeout. Si no termina a tiempo, la aborta y corre un fallback.
    private IEnumerator GameManagerWatchDog(IEnumerator phase, float maxSeconds, string label, Action onTimeout)
    {
        bool done = false;

        IEnumerator Wrapper()
        {
            while (true)
            {
                bool moveNext;
                try { moveNext = phase.MoveNext(); }
                catch (Exception ex) { done = true; Debug.LogWarning($"{label} throw: {ex}"); yield break; }
                if (!moveNext) break;
                yield return phase.Current;
            }
            done = true;
        }

        var handle = StartCoroutine(Wrapper());
        float deadline = Time.realtimeSinceStartup + maxSeconds;

        while (!done && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (!done)
        {
            StopCoroutine(handle);
            Debug.LogError($"[Timeout] {label} superó {maxSeconds:F1}s. Forzando avance.");
            onTimeout?.Invoke();
        }
    }

    private void ForceCloseDecisionPhaseOnTimeout()
    {
        isDecisionPhase = false;
        foreach (var p in players)
        {
            p.clientDecisionPhase = false;

            if (!p.isBot && p.connectionToClient != null)
                p.TargetPlayButtonAnimation(p.connectionToClient, false);

            p.RpcCancelAiming();

            if (!actionsQueue.ContainsKey(p))
                actionsQueue[p] = new PlayerAction(ActionType.None);
        }

        Debug.LogWarning("[GM] DecisionPhase forzado por timeout (faltantes -> None).");
    }


    #endregion

    #region GameCycles

    private IEnumerator RoundCycle()
    {
        yield return new WaitForSecondsRealtime(0.1f);

        while (true)
        {
            // Si DecisionPhase tarda > decisionTime + 3s, se fuerza cierre y se sigue
            yield return GameManagerWatchDog(
                DecisionPhase(),
                decisionTime + 3f,
                "[GM] DecisionPhase",
                ForceCloseDecisionPhaseOnTimeout
            );

            // Si ExecutionPhase tarda > 8s, se corta y al menos limpiamos coberturas
            yield return GameManagerWatchDog(
                ExecutionPhase(),
                decisionTime + 3f,
                "[GM] ExecutionPhase",
                ResetAllCovers
            );

            ResetAllCovers(); // limpieza normal al final de la ronda
        }
    }

    #region MisionRapida
    [Server]
    private void AssignRandomQuickMission(PlayerController player)
    {
        QuickMissionType type = (QuickMissionType) UnityEngine.Random.Range(0,Enum.GetValues(typeof(QuickMissionType)).Length);
        player.currentQuickMission = new QuickMission(type, currentRound);

        if (!player.isBot && player.connectionToClient != null)
        {
            string animName = "QM_Start_" + player.currentQuickMission.type.ToString(); // El nombre exacto de la misión como string
            player.TargetPlayAnimation(animName);
        }
    }

    private bool EvaluateQuickMission(PlayerController player, QuickMission mission)
    {
        switch (mission.type)
        {
            case QuickMissionType.DealDamage:
                return player.hasDamagedAnotherPlayerThisRound;

            case QuickMissionType.BlockShot:
                return player.wasShotBlockedThisRound;

            case QuickMissionType.ReloadAndTakeDamage:
                return actionsQueue.TryGetValue(player, out var action) && action.type == ActionType.Reload && HasTakenDamage(player);

            case QuickMissionType.DoNothing:
                return actionsQueue.TryGetValue(player, out var action2) && action2.type == ActionType.None;

            default:
                return false;
        }
    }

    [Server]
    private void ApplyQuickMissionReward(QuickMissionType missionType, PlayerController player)
    {
        switch (missionType)
        {
            case QuickMissionType.DealDamage:
                player.ammo += 2;
                break;

            case QuickMissionType.BlockShot:
                player.ServerHeal(1);
                break;

            case QuickMissionType.ReloadAndTakeDamage:
                //player.shieldBoostActivate = true; // Esto recarga los escudos al 100%, pero de momento usamos otra recompensa
                player.ServerHeal(1);
                break;

            case QuickMissionType.DoNothing:
                player.hasDoubleDamage = true;
                break;
        }
    }

    #endregion

    [Server]
    private IEnumerator DecisionPhase()
    {
        try
        {
            AdvanceTalisman();
            UpdateTikiVisual(talismanHolder);
        }
        catch (Exception e) { Debug.LogWarning($"[GM] Decision.1(Talisman/Visual): {e}"); }

        isDecisionPhase = true;

        try
        {
            foreach (var p in players)
                p.clientDecisionPhase = true;
        }
        catch (Exception e) { Debug.LogWarning($"[GM] Decision.2(SetClientFlag): {e}"); }

        currentRound++;

        try
        {
            foreach (var player in players.Concat(deadPlayers))
            {
                player.syncedRound = currentRound;
                player.canDealDamageThisRound = true;
                player.hasDamagedAnotherPlayerThisRound = false;
            }
        }
        catch (Exception e) { Debug.LogWarning($"[GM] Decision.3(InitPlayers): {e}"); }

        if (SelectedModifier == GameModifierType.CaceriaDelLider)
        {
            IdentifyVeryHealthy(); //buscar gente con mucha vida
        }

        if (missionRounds.Contains(currentRound))
        {
            // Verificamos si ya todos los vivos han recibido misión
            if (players.Where(p => p.isAlive).All(p => playersWhoHadMission.Contains(p)))
            {
                playersWhoHadMission.Clear();
            }

            if (missionQueue.Count == 0) //Llenamos la cola solo si está vacía
            {
                var shuffled = players
                    .Where (p => p.isAlive && !playersWhoHadMission.Contains(p))
                    .OrderBy(p => UnityEngine.Random.value)
                    .ToList();

                foreach (var p in shuffled)
                {
                    missionQueue.Enqueue(p);
                }
            }

            int missionsGiven = 0;

            while (missionQueue.Count > 0 && missionsGiven < missionsPerRound)
            {
                var selected = missionQueue.Dequeue();
                if (!selected.isAlive) continue;

                AssignRandomQuickMission(selected);
                playersWhoHadMission.Add(selected);
                missionsGiven++;
            }
        }

        yield return new WaitForSecondsRealtime(0.1f); //Esperar que se actualize la lista de los jugadores

        foreach (var player in players)
        {
            if (!player.isBot && player.connectionToClient != null) player.TargetPlayButtonAnimation(player.connectionToClient, true);
            player.PlayDirectionalAnimation("Idle");
        }

        actionsQueue.Clear();

        currentDecisionTime = decisionTime; //Restaurar el tiempo original
                                            //timerText.gameObject.SetActive(true);

        #region BOT_ACTION_DECISION_PHASE

        foreach (var bot in players.Where(p => p.isBot && p.isAlive))
        {
            ActionType chosenAction = ActionType.None;
            PlayerController chosenTarget = null;

            var memory = recentAttackers.ContainsKey(bot) ? recentAttackers[bot] : new Queue<PlayerController>();
            var liveAttackers = memory.Where(p => p != null && p.isAlive).Distinct().ToList();
            var enemies = players.Where(p => p != bot && p.isAlive).ToList();
            var ammoEnemies = enemies.Where(p => p.ammo > 0).ToList();
            var visibleEnemies = enemies.Where(p => !p.isCovering || p.consecutiveCovers >= 2).ToList();
            var killableWithSS = enemies.Where(p => p.health == 1).ToList();
            bool hasAmmo = bot.ammo > 0;
            bool canSuperShoot = bot.ammo >= 3;

            // Comportamiento global de SuperShoot si puede matar
            if (canSuperShoot && killableWithSS.Count > 0)
            {
                chosenTarget = killableWithSS[UnityEngine.Random.Range(0, killableWithSS.Count)];
                chosenAction = ActionType.SuperShoot;
            }
            else
            {
                //Evaluar acción basada en personalidad
                switch (bot.botPersonality)
                {
                    case BotPersonality.Shy:
                        if (liveAttackers.Count > 0 && ammoEnemies.Count > 0)
                        {
                            if (bot.consecutiveCovers < 2)
                            {
                                chosenAction = ActionType.Cover;
                                break;
                            }
                            else if (bot.consecutiveCovers >= 2 || ammoEnemies.Count == 0)
                            {
                                // Buscar a un enemigo con 1 de vida y atacarlo traicioneramente si se puede
                                var lowHpTarget = enemies.FirstOrDefault(p => p.health == 1);

                                if (lowHpTarget != null && bot.ammo > 0)
                                {
                                    chosenAction = ActionType.Shoot;
                                    chosenTarget = lowHpTarget;
                                    break;
                                }

                                chosenAction = bot.ammo < 3 ? ActionType.Reload : ActionType.Shoot;
                                break;
                            }
                        }
                        goto case BotPersonality.Vengador; //Si no hay necesidad de defenderse pasa a Vengador

                    case BotPersonality.Vengador:
                        if (liveAttackers.Count > 0 && hasAmmo)
                        {
                            chosenTarget = liveAttackers.FirstOrDefault();
                            chosenAction = canSuperShoot ? ActionType.SuperShoot : ActionType.Shoot;
                            break;
                        }
                        goto case BotPersonality.Tactico; // Si no hay venganza posible pasa a Tactico

                    case BotPersonality.Tactico:
                        if (liveAttackers.Count > 0 && ammoEnemies.Count > 0) // Si su atacante está vivo y los enemigos tienen balas, actua como Shy
                        {
                            goto case BotPersonality.Shy;
                        }
                        else if (bot.ammo <= 3)
                        {
                            chosenAction = ActionType.Reload;
                        }
                        else if (bot.ammo >= 5 && enemies.Count >0)
                        {
                            // Si está bien armado y no hay amenaza, puede hacer un SuperShoot aleatorio como presión
                            chosenTarget = enemies[UnityEngine.Random.Range(0, enemies.Count)];
                            chosenAction = ActionType.SuperShoot;
                        }
                        else
                        {
                            // Si no se lanza un supershoot, lanza un disparo de prueba a enemigos que no se han cubierto
                            var possibleTargets = visibleEnemies;
                            if (possibleTargets.Count > 0)
                            {
                                chosenTarget = possibleTargets[UnityEngine.Random.Range(0, possibleTargets.Count)];
                                chosenAction = ActionType.Shoot;
                            }
                            else
                            {
                                // Si todos están cubiertos, recarga por si acaso
                                chosenAction = ActionType.Reload;
                            }
                        }
                        
                        break;

                    case BotPersonality.Aggro:
                    default:
                        if (canSuperShoot && visibleEnemies.Count > 0)
                        {
                            chosenTarget = visibleEnemies.OrderBy(p => UnityEngine.Random.value).First();
                            chosenAction = ActionType.SuperShoot;
                        }
                        else if (hasAmmo && visibleEnemies.Count > 0)
                        {
                            chosenTarget = visibleEnemies.OrderBy(p => UnityEngine.Random.value).First();
                            chosenAction = ActionType.Shoot;
                        }
                        else if (bot.ammo < 3)
                        {
                            chosenAction = ActionType.Reload;
                        }
                        else
                        {
                            chosenAction = ActionType.Cover;
                        }
                        break;

                }
            }

            RegisterAction(bot, chosenAction, chosenTarget);
        }

        #endregion

        while (currentDecisionTime > 0)
        {
            yield return new WaitForSecondsRealtime(1f);
            currentDecisionTime = Mathf.Max(0, currentDecisionTime - 1);

            try
            {
                foreach (var player in players.Concat(deadPlayers))
                    player.syncedTimer = currentDecisionTime;
            }
            catch (Exception e) { Debug.LogWarning($"[GM] Decision.9(UpdateTimers): {e}"); }
        }

        isDecisionPhase = false;
        foreach (var p in players)
        {
            p.clientDecisionPhase = false;
        }

        foreach (var player in players)
        {
            if (!player.isBot)
            {
                player.TargetPlayButtonAnimation(player.connectionToClient, false);
            }

            player.RpcCancelAiming();

            if (player.currentQuickMission == null && !player.isBot)
            {
                player.TargetPlayAnimation("QM_Default_State");
            }

            if (!actionsQueue.ContainsKey(player)) // Si no se eligió acción alguna se llama a None
            {
                actionsQueue[player] = new PlayerAction(ActionType.None);
            }
        }

        yield return new WaitForSecondsRealtime(0.5f);//Tiempo para que se ejecute la animación
    }

    [Server]
    private IEnumerator ExecutionPhase()
    {
        currentDecisionTime = 0;

        #region 0) Antes de todo en ExecutionPhase, reseteo de UI
        try
        {
            foreach (var player in players)
            {
                player.RpcSetTargetIndicator(player, null);//Quitar targets marcados en los jugadores
                player.RpcResetButtonHightLight();//Quitar Highlights en botones
            }
        }catch (Exception e) { Debug.LogWarning($"[GM] Exec.1(PreResetUI): {e}"); }

        #endregion

        #region 1) Try/Catch cover
        try
        {
            //Prioridad a la accion cubrirse
            foreach (var entry in actionsQueue)
            {
                if (entry.Value.type == ActionType.Cover)
                {
                    float probability = entry.Key.coverProbabilities[Mathf.Min(entry.Key.consecutiveCovers, entry.Key.coverProbabilities.Length - 1)]; //Disminuye la probabilidad de cobertura por cada uso

                    if (entry.Key.shieldBoostActivate) //Si se cumple la mision
                    {
                        entry.Key.consecutiveCovers = 0; //Lo seteamos otra ves al 100%
                        entry.Key.shieldBoostActivate = false;
                        entry.Key.RpcUpdateCoverProbabilityUI(entry.Key.coverProbabilities[0]);
                    }

                    if (UnityEngine.Random.value <= probability)
                    {
                        entry.Key.isCovering = true; //Manejado por el serivdor
                        entry.Key.RpcUpdateCover(true);
                        entry.Key.PlayDirectionalAnimation("Cover");
                        entry.Key.consecutiveCovers++;
                        entry.Key.timesCovered++; //Sumar el contador de vecescubierto
                    }
                    else
                    {
                        entry.Key.RpcPlayAnimation("CoverFail");
                    }
                    //El servidor envía la actualizacion de UI a cada cliente
                    float updatedProbability = entry.Key.coverProbabilities[Mathf.Min(entry.Key.consecutiveCovers, entry.Key.coverProbabilities.Length - 1)];
                    entry.Key.RpcUpdateCoverProbabilityUI(updatedProbability); //Actualizar UI de probabilidad de cubrirse
                }
            }
        } catch (Exception e) { Debug.LogWarning($"[GM] Exec.2(Cover): {e}"); }

        #endregion

        #region 2) Try/Catch shoot
        Dictionary<PlayerController, List<PlayerController>> targetToShooters = new();

        try
        {
            foreach (var entry in actionsQueue)
            {
                if (entry.Value.type == ActionType.Shoot || entry.Value.type == ActionType.SuperShoot)
                {
                    var shooter = entry.Key;
                    var target = entry.Value.target;

                    if (target == null || !target.isAlive) continue;

                    if (!targetToShooters.ContainsKey(target))
                        targetToShooters[target] = new List<PlayerController>();

                    targetToShooters[target].Add(shooter);
                }
            }
        } catch (Exception e) { Debug.LogWarning($"[GM] Exec.3(TargetMap): {e}"); }

        #endregion

        //Luego aplica "Recargar" y "Disparar"
        yield return new WaitForSecondsRealtime(0.7f); //Pausa antes del tiroteo

        #region 3) Try/Catch reload - none
        try
        {
            //Aplicar recarga o None...
            foreach (var entry in actionsQueue)
            {
                switch (entry.Value.type)
                {
                    case ActionType.Reload:
                        entry.Key.ServerReload();
                        break;
                    case ActionType.None:
                        entry.Key.RpcPlayAnimation("None");
                        break;
                }

                //Recargar escudos si no te has cubierto
                if (entry.Value.type == ActionType.Reload ||
                    entry.Value.type == ActionType.Shoot ||
                    entry.Value.type == ActionType.SuperShoot)
                {
                    entry.Key.consecutiveCovers = 0; //Reinicia la posibilidad de cobertura al máximo otra ves
                    entry.Key.RpcUpdateCoverProbabilityUI(entry.Key.coverProbabilities[0]); //Actualizar UI de probabilidad de cubrirse
                }
            }
        } catch (Exception e) { Debug.LogWarning($"[GM] Exec.4(Reload/None): {e}"); }

        #endregion

        yield return new WaitForSecondsRealtime(0.7f); //Otra pausa pa' agregar tiempo

        #region 4) Try/Catch aplicar disparos
        try
        {
            //Aplicar disparos
            foreach (var target in targetToShooters.Keys)
            {
                List<PlayerController> shooters = targetToShooters[target];

                // Ejecutar disparos reales de los jugadores que eligieron disparar.
                // Si hay más de un jugador apuntando al mismo objetivo, solo el más cercano al Tiki ejecuta el disparo.
                // Este es el punto donde realmente se aplica el daño (y eventualmente la muerte).
                if (shooters.Count == 1)
                {
                    shooters[0].canDealDamageThisRound = true;
                    shooters[0].ServerAttemptShoot(target);
                }
                else
                {
                    PlayerController chosenShooter = GetClosestToTalisman(shooters);

                    foreach (var shooter in shooters)
                    {
                        if (shooter == chosenShooter)
                        {
                            shooter.canDealDamageThisRound = true;
                            shooter.ServerAttemptShoot(target);
                        }
                        else
                        {
                            // Jugadores que disparan, pero no hacen daño
                            shooter.canDealDamageThisRound = false;
                            shooter.ServerAttemptShoot(target);
                        }
                    }
                }
            }
        } catch (Exception e) { Debug.LogWarning($"[GM] Exec.5(Shoots): {e}"); }

        #endregion

        #region 5) Try/Catch misión rápida / animaciones de cierre
        try
        {
            foreach (var player in players)
            {
                player.selectedAction = ActionType.None;

                var mission = player.currentQuickMission;

                if (player.hasQMRewardThisRound && !player.isBot)
                {
                    player.hasQMRewardThisRound = false;
                    player.TargetPlayAnimation("QM_Reward_Exit");
                }

                if (mission != null && mission.assignedRound == currentRound)
                {
                    bool success = EvaluateQuickMission(player, mission);

                    if (success)
                    {
                        ApplyQuickMissionReward(mission.type, player);
                        player.hasQMRewardThisRound = true;
                    }

                    // SIEMPRE limpiamos la misión al final de la ronda
                    player.currentQuickMission = null;

                    string animSuffix = success ? "Reward" : "Exit";
                    string animName = $"QM_{animSuffix}_{mission.type}";

                    if (!player.isBot)
                    {
                        player.TargetPlayAnimation(animName);
                    }
                }
            }
        } catch (Exception e) { Debug.LogWarning($"[GM] Exec.6(QM): {e}"); }

        #endregion

        damagedPlayers.Clear(); // Permite recibir daño en la siguiente ronda
        CheckGameOver();

        #region 6) Registro de disparos para los BOTS
        try
        {
            foreach (var shooter in players)
            {
                if (!actionsQueue.ContainsKey(shooter)) continue;
                var action = actionsQueue[shooter];
                if ((action.type == ActionType.Shoot || action.type == ActionType.SuperShoot) && action.target != null)
                {
                    if (!recentAttackers.ContainsKey(action.target))
                    {
                        recentAttackers[action.target] = new Queue<PlayerController>();
                    }

                    if (action.target != shooter && shooter.isAlive)
                    {
                        recentAttackers[action.target].Enqueue(shooter);
                        if (recentAttackers[action.target].Count > 5)
                        {
                            recentAttackers[action.target].Dequeue();
                        }
                    }
                }
            }
        } catch (Exception e) { Debug.LogWarning($"[GM] Exec.7(BotMemory): {e}"); }

        #endregion

        #region 7) Try/Catch countdown de cierre
        try
        {
            foreach (var player in players)
            {
                player.wasShotBlockedThisRound = false; //Limpiar el bool para poder volver a usar

                if (!isGameOver && player.isAlive)
                {
                    // Mostrar la cuenta regresiva en todos los clientes
                    player.RpcShowCountdown(executionTime);
                }
            }
        } catch (Exception e) { Debug.LogWarning($"[GM] Exec.8(Countdown): {e}"); }

        #endregion

        yield return new WaitForSecondsRealtime(executionTime);
        currentDecisionTime = decisionTime; //Devolver el valor anterior del timer
    }

    public bool IsDecisionPhase() => isDecisionPhase;//para saber cuando es Fase de decision y meter variables por ejemplo en el UpdateUI();

    [Server]
    private void UpdateTikiVisual(PlayerController newHolder)
    {
        foreach (var player in players)
        {
            player.RpcSetTikiHolder(player == newHolder);
        }
    }

    [Server]
    private void AdvanceTalisman()
    {
        var alivePlayers = players.Where(p => p.isAlive).ToList();

        if (alivePlayers.Count == 0) return;

        // Ordenar por posición visual horaria
        alivePlayers = alivePlayers.OrderBy(p => p.playerPosition).ToList();

        int currentIndex = alivePlayers.IndexOf(talismanHolder);

        if (currentIndex == -1)
        {
            talismanHolder = alivePlayers[0];
            talismanHolderNetId = talismanHolder.netId;
            tikiHistory.Add(talismanHolder);

            UpdateTikiVisual(talismanHolder);
            return;
        }

        int nextIndex = (currentIndex + 1) % alivePlayers.Count;
        PlayerController previousHolder = talismanHolder;
        talismanHolder = alivePlayers[nextIndex];
        talismanHolderNetId = talismanHolder.netId;

        // Actualizar historial
        if (!tikiHistory.Contains(previousHolder))
            tikiHistory.Add(previousHolder);

        if (!tikiHistory.Contains(talismanHolder))
            tikiHistory.Add(talismanHolder);
        else
        {
            tikiHistory.Remove(talismanHolder);
            tikiHistory.Add(talismanHolder);
        }

        if (tikiHistory.Count > 7)
            tikiHistory.RemoveAt(0);
    }


    private PlayerController GetClosestToTalisman(List<PlayerController> shooters)
    {
        // Evaluar desde el más reciente hacia el más antiguo
        for (int i = tikiHistory.Count - 1; i >= 0; i--)
        {
            if (shooters.Contains(tikiHistory[i]))
            {
                return tikiHistory[i];
            }
        }

        // Fallback: si nadie está en la lista por alguna razón
        return shooters.FirstOrDefault();
    }

    private void CheckGameOver()
    {
        if (!isGameStarted || isGameOver) return; //Evitamos seguir llamando esta función si ya acabo el juego o si no ha empezado para no seguir actualizando el startGameStatistics
        //Contar número de jugadores vivos
        int alivePlayers = players.Count(player => player.isAlive);

        //Si no queda nadie vivo, la partida se detiene
        if (alivePlayers == 0)
        {
            isDraw = true;
            isGameOver = true;

            foreach (var player in players)
            {
                player.RpcOnDeathOrDraw();
            }

            StartCoroutine(StartGameStatistics());
            StopGamePhases(); // Detiene las rondas de juego
            return;
        }

        //Si solo queda un jugador vivo, lo declaramos ganador
        if (alivePlayers == 1)
        {
            PlayerController winner = players.Find(player => player.isAlive);
            if (winner != null)
            {
                winner.RpcOnVictory();
            }

            isGameOver = true;

            StartCoroutine(StartGameStatistics());
            
            StopGamePhases(); // Detiene las rondas de juego

            return;
        }

        EnsureStartCheckForHumanOrAbort();
    }

    #region Cerrar partida si no hay humanos

    private float humanAbortCheckInterval = 10f;
    private bool humanAbortWatcherStarted = false;

    [Server]
    public void EnsureStartCheckForHumanOrAbort()
    {
        if (humanAbortWatcherStarted) return;     // <- idempotente
        humanAbortWatcherStarted = true;
        StartCoroutine(StartCheckForHumanOrAbort());
    }

    [Server]
    private IEnumerator StartCheckForHumanOrAbort()
    {
        var scene = gameObject.scene;
        var wait = new WaitForSecondsRealtime(humanAbortCheckInterval);

        while (true)
        {
            yield return wait;

            bool hasRoomPlayersInMyScene = NetworkServer.connections.Values.Any(conn =>
                conn?.identity != null &&
                conn.identity.gameObject.scene == scene &&
                conn.identity.GetComponent<CustomRoomPlayer>() != null
            );

            if (!hasRoomPlayersInMyScene)
            {
                var mh = MatchHandler.Instance;
                if (mh != null)
                {
                    mh.DestroyGameScene(scene.name, "no_customroomplayers");
                }
                yield break; // la escena se va a cerrar; paramos el watcher
            }
        }
    }

    #endregion

    private IEnumerator StartGameStatistics()
    {
        // --- 0) Cerrar partida en server ---
        isGameOver = true;

        // --- 1) Asignar deathOrder al ganador (último número) ---
        var winner = players.FirstOrDefault(p => p != null && p.isAlive);
        if (winner != null)
        {
            if (winner.deathOrder == 0) winner.deathOrder = ++deathCounter;
        }

        // --- 2) Construir la lista final: muertos (en su orden) + ganador ---
        List<PlayerController> leaderboardPlayers = new List<PlayerController>(deadPlayers);

        if (winner != null && !leaderboardPlayers.Contains(winner))
            leaderboardPlayers.Add(winner);

        leaderboardPlayers = leaderboardPlayers
            .Where(p => p != null)
            .GroupBy(p => p.playerName)   // clave visible y estable para el cierre
            .Select(g => g.First())
            .ToList();


        // DEBUG: imprime el orden final tal como saldrá en el leaderboard (solo por deathOrder desc)
        var finalOrdered = leaderboardPlayers
            .OrderByDescending(p => p.deathOrder)
            .ToList();

        var lines = finalOrdered
            .Select((p, idx) => $"{idx + 1}: {p.playerName}  (deathOrder={p.deathOrder}, alive={p.isAlive}, netId={p.netId})");

        Debug.Log("[GM] === ORDEN FINAL PARA LEADERBOARD ===\n" + string.Join("\n", lines));

        // --- 3) Empujar snapshot final a GameStatistics (sin reindexar) ---
        if (gameStatistic != null)
        {
            gameStatistic.Initialize(leaderboardPlayers);

            // --- 4) Mostrar leaderboard (ordenará por deathOrder desc) ---
            gameStatistic.ShowLeaderboard();
        }

        // Solo para modo Ranked, empuja a Firestore
        var match = MatchHandler.Instance != null ? MatchHandler.Instance.GetMatch(matchId) : null;
        bool isRanked = (match != null && match.mode == "Ranked") ||
                        (!string.IsNullOrEmpty(mode) && mode.Equals("Ranked", StringComparison.OrdinalIgnoreCase));

        if (isRanked && gameStatistic != null)
        {
            // 1) Primero, los que siguen con PlayerController (como ya hacías)
            var updatedUids = new HashSet<string>();

            foreach (var pc in finalOrdered)
            {
                if (pc == null || pc.isBot) continue;

                // Usa el UID que ya guardas
                string uid = pc.firebaseUID;
                if (string.IsNullOrEmpty(uid) && pc.ownerRoomPlayer != null)
                    uid = pc.ownerRoomPlayer.firebaseUID;
                if (string.IsNullOrEmpty(uid)) continue;

                // Toma los puntos EXACTOS que calculó GameStatistic (los del leaderboard)
                if (gameStatistic.TryGetPointsForPlayer(pc.playerName, out int delta))
                {
                    updatedUids.Add(uid);
                    StartCoroutine(FirebaseServerClient.UpdateRankedPoints(uid, delta, ok =>
                    {
                        Debug.Log(ok
                            ? $"[RankedPoints] OK connected -> {pc.playerName} ({uid}) Δ{delta}"
                            : $"[RankedPoints] FAIL connected -> {pc.playerName} ({uid}) Δ{delta}");
                    }));
                }
                else
                {
                    Debug.LogWarning($"[RankedPoints] No points in snapshot for connected {pc.playerName}");
                }
            }

            // 2) Fallback: también actualiza a los humanos que empezaron pero ya no están conectados
            foreach (var uid in startingHumans)
            {
                if (updatedUids.Contains(uid)) continue; // ya procesado arriba

                string nick = null;
                yield return StartCoroutine(FirebaseServerClient.GetNicknameFromFirestore(uid, n => nick = n));

                if (string.IsNullOrWhiteSpace(nick)) continue;

                if (gameStatistic.TryGetPointsForPlayer(nick, out int delta))
                {
                    yield return StartCoroutine(FirebaseServerClient.UpdateRankedPoints(uid, delta, ok =>
                    {
                        Debug.Log(ok
                            ? $"[RankedPoints] OK offline -> {nick} ({uid}) Δ{delta}"
                            : $"[RankedPoints] FAIL offline -> {nick} ({uid}) Δ{delta}");
                    }));
                }
                else
                {
                    Debug.LogWarning($"[RankedPoints] Nick {nick} ({uid}) no está en snapshot final (¿bot o no empezó?).");
                }
            }
        }

        yield break;
    }

    private void StopGamePhases()
    {
        if (roundCycleCoroutine != null)
        {
            StopCoroutine(roundCycleCoroutine);
            roundCycleCoroutine = null;
        }

        if (decisionPhaseCoroutine != null)
        {
            StopCoroutine(decisionPhaseCoroutine);
            decisionPhaseCoroutine = null;
        }

        if (executionPhaseCoroutine != null)
        {
            StopCoroutine(executionPhaseCoroutine);
            executionPhaseCoroutine = null;
        }
    }

#endregion

    #region SERVER
    [Server]
    public void PlayerDied(PlayerController deadPlayer)
    {
        if (!players.Contains(deadPlayer)) return;

        if (!deathBuffer.Contains(deadPlayer))
            deathBuffer.Add(deadPlayer);

        if (!isProcessingDeath)
            StartCoroutine(HandleBufferedDeaths());
    }

    private IEnumerator HandleBufferedDeaths()
    {
        isProcessingDeath = true;
        yield return new WaitForSecondsRealtime(0.05f);

        if (deathBuffer.Count == 0)
        {
            isProcessingDeath = false;
            yield break;
        }

        // Verificar si todos morirían al procesar este buffer
        int vivosRestantes = players.Count - deathBuffer.Count;
        bool todosMueren = (vivosRestantes == 0);
        bool tikiVaAMorir = talismanHolder != null && deathBuffer.Contains(talismanHolder);

        if (todosMueren && tikiVaAMorir)
        {
            // Revivir al poseedor del tiki
            talismanHolder.health = 1;
            talismanHolder.isAlive = true;
            talismanHolder.deathOrder = 0;

            // Eliminarlo del buffer y evitar que se procese como muerte
            deathBuffer.RemoveAll(p => p == talismanHolder);
        }

        int tikiPos = talismanHolder?.playerPosition ?? 0;
        int DistanciaHoraria(int from, int to) => (to - from + 6) % 6;

        var sortedDeaths = deathBuffer
            .OrderBy(p => DistanciaHoraria(tikiPos, p.playerPosition))
            .ToList();

        int totalJugadores = players.Count + deadPlayers.Count + deathBuffer.Count;

        foreach (var deadPlayer in sortedDeaths)
        {
            // HandleBufferedDeaths()
            if (deadPlayer.deathOrder == 0)
                deadPlayer.deathOrder = ++deathCounter;

            players.Remove(deadPlayer);
            if (!deadPlayers.Contains(deadPlayer)) deadPlayers.Add(deadPlayer);

            if (gameStatistic != null) gameStatistic.UpdatePlayerStats(deadPlayer);

            CheckGameOver();
            yield return new WaitForSecondsRealtime(0.1f);

            if (!isDraw)
            {
                deadPlayer.RpcOnDeath();
            }
        }

        deathBuffer.Clear();
        isProcessingDeath = false;
    }

    [SerializeField] private bool mostrarDebugDeMuertesEnInspector = true;
    [SerializeField] private List<string> ordenDeMuertesInspector = new();

    private void RegistrarMuerteDebug(PlayerController muerto)
    {
        if (!mostrarDebugDeMuertesEnInspector) return;

        string entrada = $"#{deathCounter} -> {muerto.playerName}";
        ordenDeMuertesInspector.Add(entrada);
    }


    [Server]
    private void ResetAllCovers()//Elimina coberturas al finalizar una ronda
    {
        // Buscar todos los PlayerController en la escena
        PlayerController[] allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

        foreach (var player in allPlayers)
        {
            if (player != null)
            {
                player.isCovering = false;
                player.RpcUpdateCover(false);
            }
        }
    }

    //Se supone que acá va [Server], pero no lo he puesto porque GameManager está siempre en servidor así que quiero probar como va así
    public void RegisterAction(PlayerController player, ActionType actionType, PlayerController target = null)//Registra decisiones de jugadores durante DecisionPhase()
    {
        if (!isDecisionPhase || !player.isAlive) return; //Solo se pueden elegir acciones en la fase de decisión no si estás muerto

        actionsQueue[player] = new PlayerAction(actionType, target);
        player.selectedAction = actionType; //Con esto los bots y los players siempre marcarán su selectedAction igual al actionType
    }

    #endregion

    #region GameMode Roulette

    private IEnumerator ShowRouletteBeforeStart()
    {
        float rouletteDuration = UnityEngine.Random.Range(10f, 12f);
        ChooseRandomModifier();
        int winnerIndex = (int)SelectedModifier;

        foreach (var player in players)
        {
            if (!player.isBot && player.connectionToClient != null)
                player.TargetStartRouletteWithWinner(player.connectionToClient, rouletteDuration, winnerIndex);
        }

        yield return new WaitForSecondsRealtime(rouletteDuration);

        foreach (var player in players)
        {
            if (!player.isBot && player.connectionToClient != null)
                player.TargetHideRouletteCanvas(player.connectionToClient);
        }
    }

    #endregion

    #region OnPlayerDisconnect

    public void PlayerDisconnected(PlayerController player)
    {
        actionsQueue.Remove(player);
        recentAttackers.Remove(player);
        players.Remove(player);

        if (!isGameStarted)
        {
            if (gameStatistic != null) gameStatistic.UpdatePlayerStats(player, true);
            CheckIfSceneShouldClose();
            CheckAllPlayersReady();
            return;
        }

        // ===== SIMPLIFICADO: desconexión = muerte (si no tenía orden, asígnalo) =====
        player.isAlive = false;
        if (player.deathOrder == 0) player.deathOrder = ++deathCounter;
        if (!deadPlayers.Contains(player)) deadPlayers.Add(player);
        if (gameStatistic != null) gameStatistic.UpdatePlayerStats(player, true);

        if (!isGameStarted) CheckAllPlayersReady();  // si ya estamos todos (tras restar), arranca.

        CheckIfSceneShouldClose();
        CheckGameOver();
    }

    [Server]
    public void MarkDisconnected(PlayerController p)
    {
        if (p == null) return;

        // Marcar como “muerto/desconectado” para el orden del leaderboard
        p.isAlive = false;
        if (!deadPlayers.Contains(p))
        {
            // FIX: usar pre-incremento e idempotente
            if (p.deathOrder == 0) p.deathOrder = ++deathCounter;
            deadPlayers.Add(p);
        }

        // Actualiza snapshot de stats y etiqueta “(Offline)” en UI
        if (gameStatistic != null)
            gameStatistic.UpdatePlayerStats(p, disconnected: true);
    }

    private bool _closingScene = false;

    [Server]
    private void CheckIfSceneShouldClose()
    {
        if (_closingScene == true) return;

        Scene currentScene = gameObject.scene;

        bool hasRoomPlayers = NetworkServer.connections.Values.Any(conn =>
            conn.identity != null &&
            conn.identity.GetComponent<CustomRoomPlayer>() != null &&
            conn.identity.gameObject.scene == currentScene
        );

        if (!hasRoomPlayers)
        {
            Debug.Log($"[GameManager] No quedan CustomRoomPlayers en {currentScene.name}. Cerrando escena.");

            // Destruir objetos clave como el GameManager
            NetworkServer.Destroy(gameObject);

            // Descargar la escena aditiva
            SceneManager.UnloadSceneAsync(currentScene);

            MatchHandler.Instance.DestroyGameScene(currentScene.name, "empty_scene");

            _closingScene = true;
        }
    }

    #endregion
}