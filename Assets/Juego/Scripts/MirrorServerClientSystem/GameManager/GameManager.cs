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
    [SerializeField] private float decisionTime = 10f; // Tiempo para elegir acción
    [SerializeField] private float executionTime = 4f; // Tiempo para mostrar resultados
    //[SerializeField] private TMP_Text timerText;

    private bool isDecisionPhase = true;
    private bool isGameOver = false;
    private Coroutine roundCycleCoroutine;
    private Coroutine decisionPhaseCoroutine;
    private Coroutine executionPhaseCoroutine;

    [SerializeField] public List<PlayerController> players = new List<PlayerController>(); //Lista de jugadores que entran
    [SerializeField] private List<PlayerController> deadPlayers = new List<PlayerController>(); //Lista de jugadores muertos
    private HashSet<PlayerController> damagedPlayers = new HashSet<PlayerController>(); // Para almacenar jugadores que ya recibieron daño en la ronda

    private Dictionary<PlayerController, PlayerAction> actionsQueue = new Dictionary<PlayerController, PlayerAction>();
    

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
    private int deathCounter = 1;

    [Header("Talisman-Tiki")]
    [SerializeField] private GameObject talismanIconPrefab;
    private GameObject talismanIconInstance;
    [SerializeField] private Vector3 talismanOffset = new Vector3(0, 1.5f, 0);
    [SerializeField] private float talismanMoveDuration = 0.75f;

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
        if (players.Count == 0 || SelectedModifier != GameModifierType.CaceriaDelLider) return;

        int maxHealth = players.Max(player => player.health); //Buscar la mayor cantidad de vida

        veryHealthy = players.Where(player => player.health == maxHealth).ToList(); //Filtrar los que tienen vida maxima

        foreach (var player in players)
        {
            bool shouldBeHealthy = veryHealthy.Contains(player);
            player.isVeryHealthy = shouldBeHealthy;
        }
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
        roomPlayer.linkedPlayerController = controller;

        RegisterPlayer(controller);

        Debug.Log($"[GameManager] PlayerController creado para {roomPlayer.playerName}");
    }

    [Server]
    public void RegisterPlayer(PlayerController player)
    {
        if (!players.Contains(player))
        {
            players.Add(player);
            Debug.Log($"[GameManager] Jugador registrado: {player.playerName}");

            // Verificar si podemos empezar la partida
            CheckAllPlayersReady();
        }
    }

    #endregion

    private void ChooseRandomModifier()
    {
        Array values = Enum.GetValues(typeof(GameModifierType));

        SelectedModifier = (GameModifierType)values.GetValue(UnityEngine.Random.Range(0, values.Length));
        Debug.Log($"[GameManager] Se ha elegido un modificador aleatorio: {SelectedModifier}");
    }

    private void ApplyGameModifier(GameModifierType modifier)
    {
        switch (modifier)
        {
            /*case GameModifierType.DobleAgente:
                Debug.Log("Doble Agente activado: Los jugadores eligen a dos enemigos y el disparo cae aleatoriamente.");
                foreach (var player in players) player.RpcPlayAnimation("GM_DobleAgente");
                break;*/
            case GameModifierType.CaceriaDelLider:
                Debug.Log("Cacería del Líder activado: El o los jugadores con más vidas pierden la capacidad de cubrirse.");
                foreach (var player in players) player.RpcPlayAnimation("GM_CaceriaDelLider");
                //Funciona pero en el inspector hay que seleccionar
                break;
            case GameModifierType.GatilloFacil:
                Debug.Log("Gatillo Fácil activado: Los jugadores obtienen una bala más al iniciar partida.");
                foreach (var player in players)
                {
                    player.RpcPlayAnimation("GM_GatilloFacil");
                    player.ServerReload(); // Otorgar munición extra
                }
                break; 
            case GameModifierType.BalasOxidadas:
                 Debug.Log("Balas Oxidadas activado: Todos los disparos tienen un 25% de fallar esta partida.");
                foreach (var player in players)
                {
                    player.RpcPlayAnimation("GM_BalasOxidadas");
                    player.rustyBulletsActive = true;
                }
                 break;
             /*case GameModifierType.BendicionDelArsenal:
                 Debug.Log("Bendición del Arsenal activado: Todos los jugadores reciben una bala gratis cada 3 rondas.");
                 foreach (var player in players) player.RpcPlayAnimation("GM_BendicionDelArsenal");
                 break;*/
            case GameModifierType.CargaOscura:
                Debug.Log("Carga Oscura activado: Recarga 2 balas en lugar de 1.");
                foreach (var player in players)
                {
                    player.RpcPlayAnimation("GM_CargaOscura");
                    player.isDarkReloadEnabled = true;
                }
                break;
            default:
                Debug.Log("No se ha seleccionado un modificador específico.");
                break;
        }
    }

    public void CheckAllPlayersReady()
    {
        if (!isServer || isGameStarted) return;

        Debug.Log("[GameManager] Verificando jugadores conectados...");

        MatchInfo match = MatchHandler.Instance.GetMatch(matchId);
        if (match == null) return;

        if (match.players.Count != players.Count)
        {
            Debug.Log($"[GameManager] Esperando... esperados: {match.players.Count}, instanciados: {players.Count}");
            return;
        }

        isGameStarted = true;
        FillBotsIfNeeded();
        StartCoroutine (BegingameAfterDelay()); //Usamos esto para agregar una pantalla de LOADING, para la ilusión de espera, LOL
    }

    #region BOT BOT BOT

    [Server]
    private void FillBotsIfNeeded()
    {
        MatchInfo match = MatchHandler.Instance.GetMatch(matchId);
        if (match == null) return;

        int needed = MatchHandler.MATCH_SIZE - players.Count;
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
    "Zer", "Alk", "Bren", "Cyn", "Del", "Ekr", "Fal", "Gor", "Hul", "Irn"
    };

    private string[] suffixes = {
    "trik", "vel", "dor", "gorn", "ion", "rax", "mir", "nan", "ius", "in",
    "gath", "nor", "zoth", "arn", "vex", "mon", "zul", "grim", "ros", "ther",
    "vek", "lorn", "drix", "zor", "thus", "kan", "jorn", "mok", "thar", "quinn"
    };

    private string[] fullNames = {
        "Cazaputas42", "Jajaja", "Jejeje", "Jijiji", "DonComedia", "MataPanchos", "ChamucoReload",
        "CasiTeDoy", "TukiReload", "XDxdxDxd", "lolarion", "Terreneitor", "TengoHambre", "pichulaTriste"
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
        //yield return new WaitForSeconds(3f); //Tiempo de carga falsa

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
            //RpcSpawnTalisman(talismanHolder.netId);

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
        }

        Debug.Log($"[GameManager] Modificador seleccionado: {SelectedModifier}");
        ApplyGameModifier(SelectedModifier);

        Debug.Log("Todos los jugadores han ingresado su nombre. ¡La partida puede comenzar!");
        roundCycleCoroutine = StartCoroutine(RoundCycle());

        talismanHolder = players.FirstOrDefault(p => p.isAlive); // El primero con vida
        Debug.Log($"[Talisman] Se asignó el talismán a {talismanHolder.playerName}");

        if (loadingScreen != null)
        {
            loadingScreen.HideLoading();
        }
        else
        {
            Debug.LogWarning("No se encontró el LoadingScreenManager.");
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

    #region GameCycles

    private IEnumerator RoundCycle()
    {
        yield return new WaitForSeconds(0.1f);

        while (true)
        {
            decisionPhaseCoroutine = StartCoroutine(DecisionPhase());
            yield return decisionPhaseCoroutine;

            executionPhaseCoroutine = StartCoroutine(ExecutionPhase());
            yield return executionPhaseCoroutine;
            ResetAllCovers(); //Elimina coberturas
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

    [Server]
    #endregion
    private IEnumerator DecisionPhase()
    {
        AdvanceTalisman();
        UpdateTikiVisual(talismanHolder);

        isDecisionPhase = true;

        foreach (var p in players)
        {
            p.clientDecisionPhase = true;
        }

        currentRound++;

        foreach (var player in players.Concat(deadPlayers))
        {
            player.syncedRound = currentRound;
            player.canDealDamageThisRound = true;
            player.hasDamagedAnotherPlayerThisRound = false;
        }

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
                Debug.Log("[QuickMission] Todos los vivos ya recibieron misión. Reiniciando lista.");
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

                Debug.Log($"[QuickMission] {selected.playerName} recibió misión en ronda {currentRound}");
                selected.RpcSendLogToClients($"[QuickMission] {selected.playerName} recibió misión en ronda {currentRound}");

            }
        }

        yield return new WaitForSeconds(0.1f); //Esperar que se actualize la lista de los jugadores

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

        Debug.Log("Comienza la fase de decisión. Jugadores decidiendo acciones");

        while (currentDecisionTime > 0)
        {
            yield return new WaitForSeconds(1f);
            currentDecisionTime = Mathf.Max(0, currentDecisionTime - 1);

            foreach (var player in players.Concat(deadPlayers))
            {
                player.syncedTimer = currentDecisionTime;
            }
        }

        isDecisionPhase = false;
        foreach (var p in players)
        {
            p.clientDecisionPhase = false;
        }
        Debug.Log("Finalizó el tiempo de decisión.");

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
                Debug.Log($"[GameManager] {player.playerName} no eligió ninguna acción, registrando 'None'.");
                player.RpcSendLogToClients($"{player.playerName} no eligió ninguna acción, registrando 'None'.");
            }
        }

        yield return new WaitForSeconds(0.5f);//Tiempo para que se ejecute la animación
    }

    [Server]
    private IEnumerator ExecutionPhase()
    {
        currentDecisionTime = 0;

        foreach (var player in players)
        {
            player.RpcSetTargetIndicator(player, null);//Quitar targets marcados en los jugadores
            player.RpcResetButtonHightLight();//Quitar Highlights en botones
        }

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
                    Debug.Log($"[SERVER] {entry.Key.playerName} se cubrió con éxito en el intento { entry.Key.consecutiveCovers}");
                    entry.Key.RpcSendLogToClients($"{entry.Key.playerName} se cubrió con éxito en el intento {entry.Key.consecutiveCovers}");
                }
                else
                {
                    entry.Key.RpcPlayAnimation("CoverFail");
                    Debug.Log($"[SERVER] {entry.Key.playerName} intento cubrirse, pero falló después del {entry.Key.consecutiveCovers + 1} intento");
                    entry.Key.RpcSendLogToClients($"{entry.Key.playerName} intento cubrirse, pero falló después del {entry.Key.consecutiveCovers + 1} intento");
                }
                //El servidor envía la actualizacion de UI a cada cliente
                float updatedProbability = entry.Key.coverProbabilities[Mathf.Min(entry.Key.consecutiveCovers, entry.Key.coverProbabilities.Length - 1)];
                entry.Key.RpcUpdateCoverProbabilityUI(updatedProbability); //Actualizar UI de probabilidad de cubrirse
            }
        }

        Dictionary<PlayerController, List<PlayerController>> targetToShooters = new();

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

        //Luego aplica "Recargar" y "Disparar"
        yield return new WaitForSeconds(0.7f); //Pausa antes del tiroteo

        //Aplicar recarga o None...
        foreach (var entry in actionsQueue)
        {
            switch (entry.Value.type)
            {
                case ActionType.Reload:
                    entry.Key.ServerReload();
                    break;
                /*case ActionType.Shoot:
                    entry.Key.ServerAttemptShoot(entry.Value.target);
                    entry.Key.consecutiveCovers = 0; //Reinicia la posibilidad de cobertura al máximo otra ves
                    entry.Key.RpcUpdateCoverProbabilityUI(entry.Key.coverProbabilities[0]); //Actualizar UI de probabilidad de cubrirse
                    break;
                case ActionType.SuperShoot:
                    entry.Key.ServerAttemptShoot(entry.Value.target);
                    entry.Key.consecutiveCovers = 0; //Reinicia la posibilidad de cobertura al máximo otra ves
                    entry.Key.RpcUpdateCoverProbabilityUI(entry.Key.coverProbabilities[0]); //Actualizar UI de probabilidad de cubrirse
                    break;*/
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

        yield return new WaitForSeconds(0.7f); //Otra pausa pa' agregar tiempo

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
                        Debug.Log($"[Talisman] {chosenShooter.playerName} gana la prioridad para atacar a {target.playerName}"); //Los demás fallan el tiro pero no se muestra
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
                    player.RpcSendLogToClients($"{player.playerName}¡Completaste tu misión rápida!");

                    player.hasQMRewardThisRound= true;
                }
                else
                {
                    player.RpcSendLogToClients($"{player.playerName} Fallaste tu misión rápida.");
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

        damagedPlayers.Clear(); // Permite recibir daño en la siguiente ronda

        CheckGameOver();

        #region Registro de disparos para los BOTS

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

        #endregion

        foreach (var player in players)
        {
            player.wasShotBlockedThisRound = false; //Limpiar el bool para poder volver a usar

            if (!isGameOver && player.isAlive)
            {
                // Mostrar la cuenta regresiva en todos los clientes
                player.RpcShowCountdown(executionTime);
            }
        }
        yield return new WaitForSeconds(executionTime);
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
            Debug.LogWarning("[Tiki] El portador murió. Buscando nuevo portador...");
            talismanHolder = alivePlayers[0];
            talismanHolderNetId = talismanHolder.netId;
            tikiHistory.Add(talismanHolder);

            UpdateTikiVisual(talismanHolder);
            Debug.LogWarning("[Tiki] El portador actual no está entre los vivos.");
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

        Debug.Log($"[Talisman] Ahora lo tiene {talismanHolder.playerName}");
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
        //Contar número de jugadores vivos
        int alivePlayers = players.Count(player => player.isAlive);

        //Si no queda nadie vivo, la partida se detiene
        if (alivePlayers == 0)
        {
            if (talismanHolder != null && !talismanHolder.isAlive)
            {
                Debug.Log("[Tiki] El poseedor del Tiki revive por empate.");

                // Buscar quién mató al portador del Tiki
                PlayerController killer = players.FirstOrDefault(p => p.lastShotTarget == talismanHolder);

                if (killer != null)
                {
                    killer.kills = Mathf.Max(0, killer.kills - 1); // Restar 1 kill, pero no bajarlo a negativo
                    Debug.Log($"[Tiki] Se resta 1 kill a {killer.playerName} porque el Tiki salvó a {talismanHolder.playerName}.");

                    //Forzar Canvas de muerte al que no tiene tiki
                    if (killer != talismanHolder)
                    {
                        killer.RpcOnDeath();
                    }
                }

                talismanHolder.isAlive = true;
                talismanHolder.health = 1;
                talismanHolder.RpcOnVictory();
                isGameOver = true;

                StartCoroutine(StartGameStatistics());
                StopGamePhases();
                return;
            }

            Debug.Log("Todos los jugadores han muerto. Se declara empate");
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
    }

    private IEnumerator StartGameStatistics()
    {
        // Esperar 2 segundos antes de procesar estadísticas
        yield return new WaitForSeconds(2f);

        if (gameStatistic != null)
        {
            // Combina los jugadores vivos y muertos
            List<PlayerController> allPlayers = new List<PlayerController>(players); // Jugadores vivos

            // Agrega jugadores muertos que NO están ya en la lista de jugadores vivos
            foreach (var deadPlayer in deadPlayers)
            {
                if (!allPlayers.Contains(deadPlayer))
                {
                    allPlayers.Add(deadPlayer);
                }
            }

            gameStatistic.Initialize(allPlayers); // Pasa TODOS los jugadores al leaderboard

            gameStatistic.ShowLeaderboard();
            Debug.Log("Enviando señal de activación de statsCanvas");
        }

        //Actualiza Leaderboard Ranked en Firestore
        MatchInfo match = MatchHandler.Instance.GetMatch(matchId);
        if (match != null && match.mode == "Ranked")
        {
            foreach (var p in players)
            {
                if (!p.isBot && p.kills > 0 && !string.IsNullOrEmpty(p.playerId))
                {
                    LeaderboardRankedUpdater.Instance?.AddKillsToFirestore(p.playerId, p.kills);
                }
            }
        }
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

    //Eliminar esto en etapas finales o comentar, sirve solo para el testeo
    public void SetPause(bool pauseState)
    {
        Time.timeScale = pauseState ? 0f : 1f; //Si la consola está abierta, pausamos; si está cerrada, despausamos.

        Debug.Log($"[GameManager] Pausa: {(pauseState ? "Activada" : "Desactivada")}");
    }

    #endregion

    #region SERVER
    [Server]
    public void PlayerDied(PlayerController deadPlayer)
    {
        if (!players.Contains(deadPlayer)) return; // Asegurar que no intente eliminar dos veces

        if (!deathBuffer.Contains(deadPlayer))
            deathBuffer.Add(deadPlayer);

        // Agregar un delay antes de procesar la muerte
        if (!isProcessingDeath)
            StartCoroutine(HandleBufferedDeaths());
    }

    private IEnumerator HandleBufferedDeaths()
    {
        isProcessingDeath = true;

        yield return new WaitForSeconds(0.05f); // Espera breve para juntar todos los que murieron en la ronda

        var tikiPriority = tikiHistory.ToList();

        // Ordenar según lejanía al Tiki
        var sortedDeaths = deathBuffer
            .OrderBy(p => {
                int index = tikiPriority.IndexOf(p);
                return index == -1 ? int.MaxValue : index;
            }).ToList();

        foreach (var deadPlayer in sortedDeaths)
        {
            if (deadPlayer.deathOrder == 0)
                deadPlayer.deathOrder = deathCounter++;

            if (deadPlayer.hasQMRewardThisRound)
            {
                deadPlayer.hasQMRewardThisRound = false;
                if (!deadPlayer.isBot)
                    deadPlayer.TargetPlayAnimation("QM_Reward_Exit");
            }

            deadPlayer.currentQuickMission = null;

            players.Remove(deadPlayer);
            deadPlayers.Add(deadPlayer);

            yield return new WaitForSeconds(0.1f);

            CheckGameOver();

            if (!isDraw)
            {
                deadPlayer.RpcOnDeath();
            }
        }

        deathBuffer.Clear();
        isProcessingDeath = false;
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

        Debug.Log($"{player.playerName} ha elegido {actionType}");
        player.RpcSendLogToClients($"{player.playerName} ha elegido {actionType}");

        if (actionType == ActionType.SuperShoot)
        {
            Debug.Log($"{player.playerName} ha elegido SUPER SHOOT contra {target.playerName}");
            player.RpcSendLogToClients($"{player.playerName} ha elegido SUPER SHOOT contra {target.playerName}");
        }

        if (actionType == ActionType.Shoot)
        {
            Debug.Log($"{player.playerName} ha elegido SHOOT contra {target.playerName}");
            player.RpcSendLogToClients($"{player.playerName} ha elegido SHOOT contra {target.playerName}");
        }
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

        yield return new WaitForSeconds(rouletteDuration);

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
        if (players.Contains(player))
        {
            players.Remove(player);
            if (!deadPlayers.Contains(player))
            {
                deadPlayers.Add(player); // Aquí está la clave
            }
        }

        if (gameStatistic != null)
        {
            gameStatistic.UpdatePlayerStats(player); // Guardamos su estado final
        }

        CheckIfSceneShouldClose();
        CheckGameOver();
    }

    [Server]
    private void CheckIfSceneShouldClose()
    {
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
        }
    }

    #endregion
}