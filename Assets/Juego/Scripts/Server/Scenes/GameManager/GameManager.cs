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
    public bool isGameOver = false;
    private Coroutine roundCycleCoroutine;
    private Coroutine decisionPhaseCoroutine;
    private Coroutine executionPhaseCoroutine;

    [SerializeField] public List<PlayerController> players = new List<PlayerController>(); //Lista de jugadores que entran
    [SerializeField] private List<PlayerController> deadPlayers = new List<PlayerController>(); //Lista de jugadores muertos
    private HashSet<PlayerController> damagedPlayers = new HashSet<PlayerController>(); // Para almacenar jugadores que ya recibieron daño en la ronda
    private HashSet<string> startingHumans = new HashSet<string>(); // SnapShot de humanos que inician el juego
    private HashSet<uint> startingNetIds = new HashSet<uint>(); // Snapshot de TODOS los que inician (humanos y bots)

    public IReadOnlyCollection<string> GetStartingHumans() => startingHumans;
    public IReadOnlyCollection<uint> GetStartingNetIds() => startingNetIds;

    private Dictionary<PlayerController, PlayerAction> actionsQueue = new Dictionary<PlayerController, PlayerAction>();
    private Dictionary<string, string> playerIdToUid = new Dictionary<string, string>();

    private int currentRound = 0; // Contador de rondas
                                  //[SerializeField] private TMP_Text roundText; // Texto en UI para mostrar la ronda

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

    [Header("Tiempo de gracia para eliminar partida pero primero colocar puntos")]
    private float endGraceSeconds = 5f; // tuneable 2..5
    private bool endGraceActive = false;
    private bool statsStarted = false;

    [Header("Tiempo de gracia para primero mostrar animación de victoria")]
    [SerializeField] private float leaderboardDelaySeconds = 2.0f;


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
        catch (Exception e) { LogWithTime.LogError($"[GM] IdentifyVeryHealthy() fallo: {e}"); }
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
                UpdateTikiVisual(talismanHolder);
            }
        }
    }

    #region Invoke Players

    public override void OnStartServer()
    {
        if (!isServer) return; // Solo servidor ejecuta esto

        base.OnStartServer();

        LogWithTime.Log("[GameManager] Iniciado en servidor.");

        readyTimeoutCoroutine = StartCoroutine(WaitPlayersOrAbort());
    }

    [Server]
    public void OnPlayerSceneReady(CustomRoomPlayer roomPlayer)
    {
        // 0) Preferir el link que dejó TryRejoinActiveMatchByUid
        PlayerController pc = roomPlayer.linkedPlayerController;
        Debug.Log($"[REJOINDBG][GM.OnPlayerSceneReady] connId={roomPlayer.connectionToClient?.connectionId} link={(pc ? pc.netId : 0)} t={Time.time:F3}");


        // 1) Si no hay link, buscar por identidad lógica (playerId/UID) entre vivos…
        if (pc == null)
        {
            pc = players.FirstOrDefault(p =>
                p.playerId == roomPlayer.playerId ||
                (!string.IsNullOrEmpty(roomPlayer.firebaseUID) && p.firebaseUID == roomPlayer.firebaseUID));
        }

        // 2) …y si aún no, buscar también entre muertos (espectador)
        if (pc == null)
        {
            pc = deadPlayers.FirstOrDefault(p =>
                p.playerId == roomPlayer.playerId ||
                (!string.IsNullOrEmpty(roomPlayer.firebaseUID) && p.firebaseUID == roomPlayer.firebaseUID));
        }

        // 3) Si existe PC (vivo o muerto) -> rebind/authority y sync SOLO al reconectado
        if (pc != null)
        {
            // CRP <-> PC
            pc.ownerRoomPlayer = roomPlayer;
            roomPlayer.linkedPlayerController = pc;

            // Transferencia de autoridad si cambió la conexión
            var ni = pc.netIdentity;
            var newConn = roomPlayer.connectionToClient;
            if (ni.connectionToClient != newConn)
            {
                if (ni.connectionToClient != null) ni.RemoveClientAuthority();
                ni.AssignClientAuthority(newConn);
            }

            // Asegurar que este cliente observe su PC antes de los TargetRpc
            NetworkServer.RebuildObservers(ni, false);

            // Sync dirigido (apaga Waiting/GameModeCanvas solo para el que vuelve)
            StartCoroutine(SendSyncWhenReady(pc, roomPlayer));

            LogWithTime.Log($"[GameManager] Rebind PC for {roomPlayer.playerName} (alive={pc.isAlive}) and synced UI.");
            return;
        }

        // 4) Fallback: NO hay PC existente.
        //    Solo permitir spawn si la partida NO ha empezado (evita “fantasma” en rejoin avanzado).
        if (isGameStarted)
        {
            LogWithTime.LogWarning("[GameManager] OnPlayerSceneReady: no PC found for CRP but game already started; skip spawn.");
            return;
        }

        // ---------- SPAWN INICIAL (igual que lo tenías) ----------
        // Obtener posiciones de spawn disponibles en esta escena
        List<Vector3> spawnPositions = gameObject.scene.GetRootGameObjects()
            .SelectMany(go => go.GetComponentsInChildren<NetworkStartPosition>())
            .Select(pos => pos.transform.position)
            .ToList();

        // Quitar posiciones ya ocupadas por jugadores vivos
        foreach (var pAlive in players.Where(p => p.isAlive))
        {
            Vector3 pos = pAlive.transform.position;
            spawnPositions.RemoveAll(s => Vector3.Distance(s, pos) < 0.5f);
        }

        Vector3 spawnPos = spawnPositions.Count > 0
            ? spawnPositions[UnityEngine.Random.Range(0, spawnPositions.Count)]
            : Vector3.zero;

        GameObject playerInstance = Instantiate(playerControllerPrefab, spawnPos, Quaternion.identity);
        NetworkServer.Spawn(playerInstance, roomPlayer.connectionToClient);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(playerInstance, gameObject.scene);

        PlayerController controller = playerInstance.GetComponent<PlayerController>();
        controller.gameManagerNetId = netId; // vincula con este GM
        controller.playerName = roomPlayer.playerName;
        controller.playerId = roomPlayer.playerId;
        controller.ownerRoomPlayer = roomPlayer;
        controller.firebaseUID = roomPlayer.firebaseUID;

        roomPlayer.linkedPlayerController = controller;

        RegisterPlayer(controller);
    }

    IEnumerator SendSyncWhenReady(PlayerController pc, CustomRoomPlayer roomPlayer)
    {
        var conn = roomPlayer.connectionToClient;
        float deadline = Time.time + 3f; // timeout defensivo

        // forzar rebuild y esperar a que el PC sea visible para este conn
        NetworkServer.RebuildObservers(pc.netIdentity, false);
        yield return null; // al menos 1 frame

        while (Time.time < deadline)
        {
            bool hasConn = conn != null && conn.isReady;
            bool pcOk = pc != null && pc.netIdentity != null;
            // despues
            int cid = conn != null ? conn.connectionId : -1;
            bool observed = pcOk && pc.netIdentity.observers != null && pc.netIdentity.observers.ContainsKey(cid);
            bool owned = pcOk && pc.netIdentity.connectionToClient == conn;

            // DEBUG útil para tus logs
            Debug.Log($"[GameManager] ReadySync -> pcNetId={pc.netId} observedByConn={pc.netIdentity.observers.ContainsKey(cid)} ownedByConn={(pc.netIdentity.connectionToClient == conn)}");

            if (hasConn && observed && owned) break;
            yield return null;
        }

        if (pc == null || conn == null) yield break;

        // 0) Refrescar UI de botones y estados visuales (ammo/shield) del que reingresa
        TargetAnimationModifierFor(conn, SelectedModifier);
        // 1) Boton/anim de fase (TargetRpc desde el server)
        pc.TargetPlayButtonAnimation(conn, isDecisionPhase);
        pc.TargetRefreshLocalUI(conn);
        pc.TargetResyncParcaVisual(conn);
        // 2) Si este frame estaba cubriendose, mantenerlo (ClientRpc desde el server)
        if (pc.isCovering)            // ajusta el nombre si tu flag es distinto
            pc.RpcUpdateCover(true);
        // 3) Feedback corto "CoverBroken" si hubo bloqueo previo y YA no esta cubriendose
        if (pc.wasShotBlockedThisRound && !pc.isCovering)  // ajusta nombres si difieren
            pc.RpcForcePlayAnimation("CoverBroken");

        // Si vuelve muerto, fuerza su UI de muerte (no se re-reproduce RpcOnDeath)
        if (!pc.isAlive) pc.TargetApplyDeathUI(conn);

        // ligar explícitamente CRP <-> PC (sección 2)
        LinkCrpWithPc(roomPlayer, pc);
    }

    [Server]
    void LinkCrpWithPc(CustomRoomPlayer roomPlayer, PlayerController pc)
    {
        roomPlayer.linkedPlayerController = pc;                                    // tu referencia de servidor
        roomPlayer.RpcSetLinkedPc(roomPlayer.connectionToClient, pc.netIdentity);  // notifica al cliente local cuál es su PC
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

            bool isRanked = false;
            var match = MatchHandler.Instance?.GetMatch(matchId);
            if (match != null) isRanked = string.Equals(match.mode, "Ranked", StringComparison.OrdinalIgnoreCase);
            else if (!string.IsNullOrEmpty(mode)) isRanked = string.Equals(mode, "Ranked", StringComparison.OrdinalIgnoreCase);

            player.hideNameInRanked = isRanked;

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
                foreach (var player in players)
                {
                    var conn = player.ownerRoomPlayer?.connectionToClient;
                    if (!player.isBot && conn != null)
                    {
                        player.RpcPlayAnimation("GM_CaceriaDelLider");
                    }
                }
                break;
            case GameModifierType.GatilloFacil:
                foreach (var player in players)
                {
                    var conn = player.ownerRoomPlayer?.connectionToClient;
                    if (!player.isBot && conn != null)
                    {
                        player.RpcPlayAnimation("GM_GatilloFacil");
                    }
                    player.ServerReload(); // Otorgar munición extra
                }
                break; 
            case GameModifierType.BalasOxidadas:
                foreach (var player in players)
                {
                    var conn = player.ownerRoomPlayer?.connectionToClient;
                    if (!player.isBot && conn != null)
                    {
                        player.RpcPlayAnimation("GM_BalasOxidadas");
                    }
                    player.rustyBulletsActive = true;
                }
                 break;
             /*case GameModifierType.BendicionDelArsenal:
                 foreach (var player in players) player.RpcPlayAnimation("GM_BendicionDelArsenal");
                 break;*/
            case GameModifierType.CargaOscura:
                foreach (var player in players)
                {
                    var conn = player.ownerRoomPlayer?.connectionToClient;
                    if (!player.isBot && conn != null)
                    {
                        player.RpcPlayAnimation("GM_CargaOscura");
                    }
                    player.isDarkReloadEnabled = true;
                }
                break;
            default:
                break;
        }
    }

    // GameManager
    [Server]
    private void TargetAnimationModifierFor(NetworkConnectionToClient target, GameModifierType modifier)
    {
        foreach (var p in players)
        {
            if (p.isBot) continue;
            switch (modifier)
            {
                case GameModifierType.CaceriaDelLider: p.TargetPlayAnimation(target, "GM_CaceriaDelLider"); break;
                case GameModifierType.GatilloFacil: p.TargetPlayAnimation(target, "GM_GatilloFacil"); break;
                case GameModifierType.BalasOxidadas: p.TargetPlayAnimation(target, "GM_BalasOxidadas"); break;
                case GameModifierType.CargaOscura: p.TargetPlayAnimation(target, "GM_CargaOscura"); break;
            }
        }
    }

    public void CheckAllPlayersReady()
    {
        if (!isServer || isGameStarted) return;

        MatchInfo match = MatchHandler.Instance.GetMatch(matchId);
        if (match == null) return;

        if (match.players.Count != players.Count)
        {
            LogWithTime.Log($"[GameManager] Esperando... esperados: {match.players.Count}, instanciados: {players.Count}");
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

        if (mode == "Ranked")
        {
            foreach (var pc in players.Where(p => !p.isBot))
            {
                string uid = pc.firebaseUID;
                if (string.IsNullOrEmpty(uid) && pc.ownerRoomPlayer != null)
                    uid = pc.ownerRoomPlayer.firebaseUID;

                if (!string.IsNullOrEmpty(uid))
                {
                    StartCoroutine(FirebaseServerClient.TryConsumeTicket(uid, success =>
                    {
                        if (!success)
                        {
                            LogWithTime.LogWarning($"[Ticket] FALLO al cobrar ticket de {pc.playerName} ({uid})");
                            // Aqui puedes decidir si expulsar al jugador o marcarlo como invalido
                        }
                        else
                        {
                            LogWithTime.Log($"[Ticket] Cobrado ticket de {pc.playerName} ({uid})");
                            StartCoroutine(FirebaseServerClient.FetchTicketAndKeyInfoFromWallet(uid, (t, k) =>
                            {
                                pc.ownerRoomPlayer?.TargetReceiveWalletData(pc.ownerRoomPlayer.connectionToClient, t, k);
                            }));
                        }
                    }));
                }
            }
        }


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
        int humanExpected =
        (startingHumans != null && startingHumans.Count > 0)
        ? startingHumans.Count
        : match.players.Count; // fallback humanos asignados a la sala

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
            {
                p.isRouletteOpen = false;

                var conn = p.ownerRoomPlayer?.connectionToClient;
                if (!p.isBot && conn != null)
                {
                    p.TargetHideRouletteCanvas(conn);
                }
            }
        }


        ApplyGameModifier(SelectedModifier);

        roundCycleCoroutine = StartCoroutine(RoundCycle());

        talismanHolder = players.FirstOrDefault(p => p.isAlive); // El primero con vida
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
                catch (Exception ex) { done = true; LogWithTime.LogWarning($"{label} throw: {ex}"); yield break; }
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
            LogWithTime.LogError($"[Timeout] {label} superó {maxSeconds:F1}s. Forzando avance.");
            onTimeout?.Invoke();
        }
    }

    private void ForceCloseDecisionPhaseOnTimeout()
    {
        isDecisionPhase = false;
        foreach (var p in players)
        {
            p.clientDecisionPhase = false;

            var conn = p.ownerRoomPlayer != null ? p.ownerRoomPlayer.connectionToClient : null;
            if (!p.isBot && conn != null)
                p.TargetPlayButtonAnimation(conn, false);

            p.RpcCancelAiming();

            if (!actionsQueue.ContainsKey(p))
                actionsQueue[p] = new PlayerAction(ActionType.None);
        }

        LogWithTime.LogWarning("[GM] DecisionPhase forzado por timeout (faltantes -> None).");
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

        var conn = player.ownerRoomPlayer?.connectionToClient;
        if (!player.isBot && conn != null)
        {
            string animName = "QM_Start_" + player.currentQuickMission.type.ToString(); // El nombre exacto de la misión como string
            player.TargetPlayAnimation(conn, animName);
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
        catch (Exception e) { LogWithTime.LogWarning($"[GM] Decision.1(Talisman/Visual): {e}"); }

        isDecisionPhase = true;

        try
        {
            foreach (var p in players)
            {
                p.clientDecisionPhase = true;
                p.RpcClearKillFeed();
            }
            foreach (var d in deadPlayers)
            {
                d.RpcClearKillFeed();
            }
        }
        catch (Exception e) { LogWithTime.LogWarning($"[GM] Decision.2(SetClientFlag): {e}"); }

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
        catch (Exception e) { LogWithTime.LogWarning($"[GM] Decision.3(InitPlayers): {e}"); }

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
            var conn = player.ownerRoomPlayer ?.connectionToClient;
            if (!player.isBot && conn != null) player.TargetPlayButtonAnimation(conn, true);
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

            bool coverBlocked = (SelectedModifier == GameModifierType.CaceriaDelLider && bot.isVeryHealthy);

            // Early exit para DoNothing inteligente
            if (BotShouldDoNothingThisRound(bot) && BotShouldAcceptDoNothing(bot, enemies))
            {
                RegisterAction(bot, ActionType.None, null);
                continue; // salta el resto y no cae en ActionType.None accidental
            }

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
                        {
                            if (liveAttackers.Count > 0 && ammoEnemies.Count > 0)
                            {
                                // Antes: chosenAction = ActionType.Cover
                                chosenAction = (!coverBlocked && bot.consecutiveCovers < 2) ? ActionType.Cover
                                              : ((bot.ammo > 0) ? ActionType.Shoot : ActionType.Reload);

                                if (chosenAction == ActionType.Shoot && chosenTarget == null)
                                    chosenTarget = PickAnyVisibleOrAny(visibleEnemies, enemies);
                                break;
                            }

                            var lowHpTarget = enemies.FirstOrDefault(p => p.health == 1);
                            if (lowHpTarget != null && bot.ammo > 0)
                            {
                                chosenAction = ActionType.Shoot;
                                chosenTarget = lowHpTarget;
                                break;
                            }
                            goto case BotPersonality.Vengador; //Si no hay necesidad de defenderse pasa a Vengador
                        }

                    case BotPersonality.Vengador:
                        {
                            if (liveAttackers.Count > 0 && hasAmmo)
                            {
                                var revenge = liveAttackers.LastOrDefault();
                                chosenTarget = revenge ?? PickAnyVisibleOrAny(visibleEnemies, enemies);
                                chosenAction = canSuperShoot ? ActionType.SuperShoot : ActionType.Shoot;
                                break;
                            }
                            goto case BotPersonality.Tactico;
                        }

                    case BotPersonality.Tactico:
                        {
                            if (liveAttackers.Count > 0 && ammoEnemies.Count > 0)
                            {
                                // Se comporta como Shy en amenaza, pero con veto cover
                                chosenAction = (!coverBlocked && bot.consecutiveCovers < 2) ? ActionType.Cover
                                              : ((bot.ammo > 0) ? ActionType.Shoot : ActionType.Reload);

                                if (chosenAction == ActionType.Shoot && chosenTarget == null)
                                    chosenTarget = PickAnyVisibleOrAny(visibleEnemies, enemies);
                                break;
                            }
                            else if (bot.ammo <= 3)
                            {
                                chosenAction = ActionType.Reload;
                            }
                            else if (bot.ammo >= 5 && enemies.Count > 0)
                            {
                                chosenTarget = enemies[UnityEngine.Random.Range(0, enemies.Count)];
                                chosenAction = ActionType.SuperShoot;
                            }
                            else
                            {
                                PlayerController weakest = null;
                                int minHealth = int.MaxValue;

                                foreach (var enemy in visibleEnemies)
                                {
                                    if (enemy.health < minHealth)
                                    {
                                        minHealth = enemy.health;
                                        weakest = enemy;
                                    }
                                }

                                chosenTarget = (weakest != null) ? weakest : PickAnyVisibleOrAny(visibleEnemies, enemies);

                                if (chosenTarget != null)
                                    chosenAction = ActionType.Shoot;
                                else
                                    chosenAction = ActionType.Reload;
                            }
                            break;
                        }

                    case BotPersonality.Aggro:
                    default:
                        {
                            if (canSuperShoot && (visibleEnemies.Count > 0 || enemies.Count > 0))
                            {
                                chosenTarget = PickAnyVisibleOrAny(visibleEnemies, enemies);
                                chosenAction = ActionType.SuperShoot;
                            }
                            else if (hasAmmo && (visibleEnemies.Count > 0 || enemies.Count > 0))
                            {
                                chosenTarget = PickAnyVisibleOrAny(visibleEnemies, enemies);
                                chosenAction = ActionType.Shoot;
                            }
                            else if (bot.ammo < 3)
                            {
                                chosenAction = ActionType.Reload;
                            }
                            else
                            {
                                // Antes: Cover; ahora respeta el veto
                                chosenAction = (!coverBlocked && bot.consecutiveCovers < 2) ? ActionType.Cover
                                              : ((bot.ammo > 0) ? ActionType.Shoot : ActionType.Reload);

                                if (chosenAction == ActionType.Shoot && chosenTarget == null)
                                    chosenTarget = PickAnyVisibleOrAny(visibleEnemies, enemies);
                            }
                            break;
                        }
                }
            }

            // Sanity guards for Shoot
            if ((chosenAction == ActionType.Shoot || chosenAction == ActionType.SuperShoot) && bot.ammo <= 0)
            {
                chosenAction = ActionType.Reload;
                chosenTarget = null;
            }

            if ((chosenAction == ActionType.Shoot || chosenAction == ActionType.SuperShoot) && chosenTarget == null)
            {
                if (hasAmmo && visibleEnemies.Count > 0)
                {
                    chosenTarget = visibleEnemies[UnityEngine.Random.Range(0, visibleEnemies.Count)];
                }
                else if (!hasAmmo)
                {
                    chosenAction = ActionType.Reload;
                }
                else
                {
                    chosenAction = ActionType.None;
                }
            }

            // ----- Dumbness nerf: 70% chance to switch target to another valid one -----
            if (botsEnableRandomSwitch && (chosenAction == ActionType.Shoot || chosenAction == ActionType.SuperShoot))
            {
                if (chosenTarget != null && UnityEngine.Random.value < botRandomSwitchChance)
                {
                    // visibleEnemies y enemies ya existen en este scope
                    var alt = PickAlternativeTarget(chosenTarget, visibleEnemies, enemies);
                    chosenTarget = alt; // may remain if no alternative
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
            catch (Exception e) { LogWithTime.LogWarning($"[GM] Decision.9(UpdateTimers): {e}"); }
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
                var conn = player.ownerRoomPlayer?.connectionToClient;
                player.TargetPlayButtonAnimation(conn, false);
            }

            player.RpcCancelAiming();

            if (player.currentQuickMission == null && !player.isBot)
            {
                var conn = player.ownerRoomPlayer?.connectionToClient;
                player.TargetPlayAnimation(conn, "QM_Default_State");
            }

            if (!actionsQueue.ContainsKey(player)) // Si no se eligió acción alguna se llama a None
            {
                actionsQueue[player] = new PlayerAction(ActionType.None);
            }
        }

        yield return new WaitForSecondsRealtime(0.5f);//Tiempo para que se ejecute la animación
    }

    #region Bot_bot_bot

    PlayerController PickAnyVisibleOrAny(List<PlayerController> vis, List<PlayerController> all)
    {
        var pickFrom = (vis.Count > 0) ? vis : all;
        return (pickFrom.Count > 0) ? pickFrom[UnityEngine.Random.Range(0, pickFrom.Count)] : null;
    }

    private bool BotShouldDoNothingThisRound(PlayerController bot)
    {
        var m = bot.currentQuickMission;
        if (m == null) return false;
        if (m.assignedRound != currentRound) return false;
        return m.type == QuickMissionType.DoNothing;
    }

    private bool BotShouldAcceptDoNothing(PlayerController bot, List<PlayerController> enemies)
    {
        // Heuristica: aceptar si
        // - tiene >=1 bala (podra pegar "doble dano" la proxima), o
        // - tiene 2+ de vida y pocos enemigos con ammo (sobrevive el turno)
        // - NO esta siendo foco de multiples (memoria de atacantes)
        bool hasAmmoNextRound = (bot.ammo >= 1);
        int threats = enemies.Count(e => e.ammo > 0);
        bool likelySurvive = bot.health >= 2 && threats <= 2;

        // Opcional: si hay muchos visibles apuntando/le han atacado, no conviene esperar
        var mem = recentAttackers.ContainsKey(bot) ? recentAttackers[bot] : new Queue<PlayerController>();
        int recentAliveAttackers = mem.Count(p => p != null && p.isAlive);

        if (hasAmmoNextRound) return true;
        if (likelySurvive && recentAliveAttackers == 0) return true;

        return false;
    }

    // ----- Bot nerf -----
    [Header("Bot Nerf")]
    [SerializeField] private bool botsEnableRandomSwitch = true;
    [Range(0f, 1f)][SerializeField] private float botRandomSwitchChance = 0.7f;


    // Returns an alternative target different from current if possible.
    private PlayerController PickAlternativeTarget(PlayerController current, List<PlayerController> visible, List<PlayerController> all)
    {
        // Prefer visible pool if available
        var pool = (visible != null && visible.Count > 0) ? visible : all;
        if (pool == null || pool.Count == 0) return current;

        // Build candidates excluding current
        var candidates = pool.Where(p => p != null && p.isAlive && p != current).ToList();
        if (candidates.Count == 0) return current;

        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    #endregion

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
        }catch (Exception e) { LogWithTime.LogWarning($"[GM] Exec.1(PreResetUI): {e}"); }

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
        } catch (Exception e) { LogWithTime.LogWarning($"[GM] Exec.2(Cover): {e}"); }

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
        } catch (Exception e) { LogWithTime.LogWarning($"[GM] Exec.3(TargetMap): {e}"); }

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
        } catch (Exception e) { LogWithTime.LogWarning($"[GM] Exec.4(Reload/None): {e}"); }

        #endregion

        yield return new WaitForSecondsRealtime(0.7f); //Otra pausa pa' agregar tiempo

        #region 4) Try/Catch aplicar disparos (tiki cascade)
        try
        {
            //Aplicar disparos
            foreach (var target in targetToShooters.Keys)
            {
                List<PlayerController> shooters = targetToShooters[target];

                if (shooters == null || shooters.Count == 0) continue;

                if (AllowAccumulatedDamage())
                {
                    foreach (var shooter in shooters)
                    {
                        if (!target.isAlive) break; // target may die mid-loop
                        shooter.canDealDamageThisRound = true;
                        shooter.ServerAttemptShoot(target);
                    }
                    continue;
                }

                // CASCADE: only one can deal damage; if earlier shooter fails, give a chance to the next
                var ordered = GetShootersByTikiOrder(shooters);

                bool damageHappened = false;

                foreach (var shooter in ordered)
                {
                    if (!damageHappened)
                    {
                        // Try to actually deal damage
                        shooter.canDealDamageThisRound = true;
                        shooter.ServerAttemptShoot(target);

                        // After attempt, check if damage was registered for the target in this round
                        if (HasTakenDamage(target))
                        {
                            damageHappened = true;
                        }
                    }
                    else
                    {
                        // Visual only (no damage). Keeps current feel for non-priority shooters.
                        shooter.canDealDamageThisRound = false;
                        shooter.ServerAttemptShoot(target);
                    }
                }
            }
        } catch (Exception e) { LogWithTime.LogWarning($"[GM] Exec.5(Shoots): {e}"); }

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

                    var conn = player.ownerRoomPlayer?.connectionToClient;
                    player.TargetPlayAnimation(conn, "QM_Reward_Exit");
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
                        var conn = player.ownerRoomPlayer?.connectionToClient;
                        player.TargetPlayAnimation(conn, animName);
                    }
                }
            }
        } catch (Exception e) { LogWithTime.LogWarning($"[GM] Exec.6(QM): {e}"); }

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
        } catch (Exception e) { LogWithTime.LogWarning($"[GM] Exec.7(BotMemory): {e}"); }

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
        } catch (Exception e) { LogWithTime.LogWarning($"[GM] Exec.8(Countdown): {e}"); }

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

    private List<PlayerController> GetShootersByTikiOrder(List<PlayerController> shooters)
    {
        // Build ordered list according to tikiHistory (newest priority last -> iterate backward)
        var ordered = new List<PlayerController>();

        for (int i = tikiHistory.Count - 1; i >= 0; i--)
        {
            var p = tikiHistory[i];
            if (p != null && shooters.Contains(p) && !ordered.Contains(p))
                ordered.Add(p);
        }

        // Append any remaining shooters not present in tikiHistory (fallback)
        foreach (var s in shooters)
            if (!ordered.Contains(s))
                ordered.Add(s);

        return ordered;
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
                CerrarAnimacionesMision(player);
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

    private float humanAbortCheckInterval = 3f;
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
                // No humans (no CRP) -> force a clean end instead of destroying now
                if (!isGameOver)
                {
                    // mark disconnected humans as dead for ordering
                    foreach (var h in players.Where(p => !p.isBot && p.isAlive).ToList())
                        ExpulsionOrQuit(h);

                    // kill bots leaving one alive
                    var botsAlive = players.Where(p => p.isBot && p.isAlive).ToList();
                    if (botsAlive.Count > 1)
                    {
                        for (int i = 0; i < botsAlive.Count - 1; i++)
                            PlayerDied(botsAlive[i]);
                    }

                    // if still more than 1 alive for some reason, fallback to draw
                    int alive = players.Count(p => p.isAlive);
                    if (alive > 1)
                    {
                        // declare draw
                        foreach (var p in players) p.isAlive = false;
                    }

                    isGameOver = true;
                    StopGamePhases();

                    yield return StartCoroutine(StartGameStatistics());
                }

                yield break;
            }
        }
    }

    #endregion

    private IEnumerator StartGameStatistics()
    {
        if (statsStarted) yield break; // idempotente
        statsStarted = true;

        // --- 0) Cerrar partida en server ---
        isGameOver = true;

        // 0.b) Tiempo de gracia para procesos secundarios, desconexión, banderas, victoria, etc
        if (leaderboardDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(leaderboardDelaySeconds);
        }

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
            .GroupBy(p => string.IsNullOrEmpty(p.firebaseUID) ? p.playerName : p.firebaseUID)
            .Select(g => g.OrderByDescending(p => p.deathOrder).First())
            .ToList();

        // DEBUG: imprime el orden final tal como saldrá en el leaderboard (solo por deathOrder desc)
        var finalOrdered = leaderboardPlayers
            .OrderByDescending(p => p.isAlive).ThenByDescending(p => p.deathOrder)
            .ToList();

        var lines = finalOrdered
            .Select((p, idx) => $"{idx + 1}: {p.playerName}  (deathOrder={p.deathOrder}, alive={p.isAlive}, netId={p.netId})");

        LogWithTime.Log("[GM] === ORDEN FINAL PARA LEADERBOARD ===\n" + string.Join("\n", lines));

        // --- 3) Empujar snapshot final a GameStatistics (sin reindexar) ---
        if (gameStatistic != null)
        {
            gameStatistic.Initialize(leaderboardPlayers, true);

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

                // mark played-ranked always
                yield return StartCoroutine(FirebaseServerClient.SetHasPlayedRanked(uid, true, null));

                int delta;
                if (gameStatistic.TryGetPointsByUid(uid, out delta))
                {
                    updatedUids.Add(uid);
                    yield return StartCoroutine(FirebaseServerClient.UpdateRankedPoints(uid, delta, ok =>
                    {
                        LogWithTime.Log(ok
                            ? $"[RankedPoints] OK connected -> {pc.playerName} ({uid}) delta={delta}"
                            : $"[RankedPoints] FAIL connected -> {pc.playerName} ({uid}) delta={delta}");
                    }));
                }
                else
                {
                    LogWithTime.LogWarning($"[RankedPoints] No points for UID {uid} (connected: {pc.playerName})");
                }
            }

            // 2) Fallback: también actualiza a los humanos que empezaron pero ya no están conectados
            foreach (var uid in startingHumans)
            {
                if (updatedUids.Contains(uid)) continue;

                // mark played-ranked always
                yield return StartCoroutine(FirebaseServerClient.SetHasPlayedRanked(uid, true, null));

                int delta;
                if (gameStatistic.TryGetPointsByUid(uid, out delta))
                {
                    yield return StartCoroutine(FirebaseServerClient.UpdateRankedPoints(uid, delta, ok =>
                    {
                        LogWithTime.Log(ok
                            ? $"[RankedPoints] OK offline -> ({uid}) delta={delta}"
                            : $"[RankedPoints] FAIL offline -> ({uid}) delta={delta}");
                    }));
                }
                else
                {
                    LogWithTime.LogWarning($"[RankedPoints] UID {uid} not found in final snapshot.");
                }
            }

            // 2.1 Grant a basic key to the winner (Ranked only, human only)
            {
                // winner is the top by deathOrder (already built above)
                var winnerForKey = finalOrdered
                    .OrderByDescending(p => p.deathOrder)
                    .FirstOrDefault();

                if (winnerForKey != null && !winnerForKey.isBot)
                {
                    string wuid = winnerForKey.firebaseUID;
                    if (string.IsNullOrEmpty(wuid) && winnerForKey.ownerRoomPlayer != null)
                        wuid = winnerForKey.ownerRoomPlayer.firebaseUID;

                    if (!string.IsNullOrEmpty(wuid))
                    {
                        yield return StartCoroutine(FirebaseServerClient.GrantKeyToPlayer(wuid, ok =>
                        {
                            LogWithTime.Log(ok
                                ? $"[Key] OK -> {winnerForKey.playerName} ({wuid})"
                                : $"[Key] FAIL -> {winnerForKey.playerName} ({wuid})");
                        }));
                    }
                    else
                    {
                        LogWithTime.LogWarning("[Key] Winner has no UID, skip key grant.");
                    }
                }
                else
                {
                    LogWithTime.Log("[Key] Winner is a bot or null, skip key grant.");
                }
            }
        }

        yield return new WaitForSecondsRealtime(0.2f); // darle 1-2 frames al RPC del leaderboard

        var myScene = gameObject.scene;
        foreach (var crp in UnityEngine.Object.FindObjectsByType<CustomRoomPlayer>(FindObjectsSortMode.None))
        {
            if (crp != null && crp.gameObject.scene == myScene)
            {
                // Esto mueve el CRP a MainScene, destruye su PlayerController y mantiene visible el canvas del CRP
                crp.ServerReturnToMainMenu();
            }
        }

        yield return new WaitForSecondsRealtime(1f);

        // schedule scene destroy after grace window
        StartCoroutine(DestroySceneAfterGrace("end_grace"));
        yield break;
    }

    [Server]
    private IEnumerator DestroySceneAfterGrace(string reason)
    {
        if (endGraceActive) yield break;
        endGraceActive = true;

        var sceneName = gameObject.scene.name;
        LogWithTime.Log($"[Grace] Esperando {endGraceSeconds:F1}s antes de destruir '{sceneName}' ({reason})...");
        yield return new WaitForSecondsRealtime(endGraceSeconds);
        LogWithTime.Log($"[Grace] Tiempo cumplido. Destruyendo '{sceneName}' ahora ({reason}).");

        _closingScene = true;
        MatchHandler.Instance?.DestroyGameScene(sceneName, reason);
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
                CerrarAnimacionesMision(deadPlayer);
                deadPlayer.RpcOnDeath();
            }
        }

        deathBuffer.Clear();
        isProcessingDeath = false;
    }

    // NUEVO: cierre genérico de overlays/animaciones “transitorias”
    [Server]
    private void CerrarAnimacionesMision(PlayerController p)
    {
        if (p == null || p.isBot) return;

        try
        {
            // 1) Si estaba abierto el reward de QM, cerrarlo
            if (p.hasQMRewardThisRound)
            {
                p.hasQMRewardThisRound = false;

                var conn = p.ownerRoomPlayer?.connectionToClient;
                p.TargetPlayAnimation(conn,"QM_Reward_Exit"); // cierra el reward suavemente
            }

            // 2) Si la QM de este round estaba activa, disparar su Exit y limpiar
            var mission = p.currentQuickMission;
            if (mission != null && mission.assignedRound == currentRound)
            {
                string animName = $"QM_Exit_{mission.type}";
                p.currentQuickMission = null; // no evaluamos ni damos recompensa al morir

                var conn = p.ownerRoomPlayer?.connectionToClient;
                p.TargetPlayAnimation(conn, animName);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[GM] ForceCloseTransientUI: {e}");
        }
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
        if (player.isAfk) player.selectedAction = ActionType.None; // Si está Afk elegirá nada 

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
            player.isRouletteOpen = true;

            var conn = player.ownerRoomPlayer?.connectionToClient;
            if (!player.isBot && conn != null)
                player.TargetStartRouletteWithWinner(conn, rouletteDuration, winnerIndex);
        }

        yield return new WaitForSecondsRealtime(rouletteDuration);

        foreach (var player in players)
        {
            player.isRouletteOpen = false;

            var conn = player.ownerRoomPlayer?.connectionToClient;
            if (!player.isBot && conn != null)
            {
                player.TargetHideRouletteCanvas(conn);
            }
        }
    }

    #endregion

    #region OnPlayerDisconnect

    
    public void ExpulsionOrQuit(PlayerController player)
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

        CheckGameOver();
    }

    
    [Server]
    public void PlayerWentAfk(PlayerController p)
    {
        if (p == null) return;
       
        p.isAfk = true;

        // Actualiza snapshot de stats y etiqueta “(Offline)” en UI
        if (gameStatistic != null)
            gameStatistic.UpdatePlayerStats(p, disconnected: true);

        MaybeAllPlayerWentAfk();
    }

    [Server]
    private void MaybeAllPlayerWentAfk()
    {
        if (isGameOver) return;

        // Vivos humanos (players == vivos por diseno)
        var aliveHumans = players.Where(p => !p.isBot).ToList();
        if (aliveHumans.Count == 0) return;

        // Espectadores humanos CONECTADOS (muertos que siguen con CRP/conn activa)
        bool hasConnectedSpectator = deadPlayers.Any(p =>
            p != null &&
            !p.isBot &&
            p.ownerRoomPlayer != null &&
            p.ownerRoomPlayer.connectionToClient != null &&
            p.ownerRoomPlayer.connectionToClient.isReady
            );

        // Si hay espectadores conectados, NO cerrar por AFK
        if (hasConnectedSpectator)
        {
            LogWithTime.Log("[AFK] Hold: connected human spectator present.");
            return;
        }

        // Si todos los vivos estan AFK y no hay espectadores conectados -> fin por AFK
        if (aliveHumans.All(h => h.isAfk))
        {
            // Asegura orden en leaderboard (si alguno vivo no tenía deathOrder)
            foreach (var h in aliveHumans)
            {
                if (h.deathOrder == 0) h.deathOrder = ++deathCounter;
                if (!deadPlayers.Contains(h)) deadPlayers.Add(h);
                gameStatistic?.UpdatePlayerStats(h, true);
            }

            // Si quieres terminar ya (todos AFK), dispara tu final estándar:
            isGameOver = true;
            StopAllCoroutines(); // o tu StopGamePhases()
            StartCoroutine(StartGameStatistics()); // tu final habitual
        }
    }

    private bool _closingScene = false;

    [Server]
    private void CheckIfSceneShouldClose()
    {
        if (_closingScene) return;

        // si estamos en gracia o ya marcamos fin, no cierres por aqui
        if (endGraceActive || isGameOver) return;

        Scene currentScene = gameObject.scene;

        bool hasRoomPlayers = NetworkServer.connections.Values.Any(conn =>
            conn.identity != null &&
            conn.identity.GetComponent<CustomRoomPlayer>() != null &&
            conn.identity.gameObject.scene == currentScene
        );

        if (!hasRoomPlayers)
        {
            // Si la partida todavia NO empezo, limpia de inmediato (setup abortado)
            if (!isGameStarted)
            {
                LogWithTime.Log($"[GameManager] No CRP before game start in {currentScene.name}. Closing scene.");
                NetworkServer.Destroy(gameObject);
                SceneManager.UnloadSceneAsync(currentScene);
                MatchHandler.Instance.DestroyGameScene(currentScene.name, "empty_scene_not_started");
                _closingScene = true;
            }
            // Si ya empezo, NO destruyas aqui; el watcher de 'no humanos' cerrara con gracia.
            return;
        }

        // Si hay humanos, nunca cierres aqui.
    }

    #endregion


    #region KillFeed

    [Server]
    public void ServerRelayKill(string killerName, string victimName)
    {
        foreach (var player in players)
        {
            player.RpcAnnounceKill(killerName, victimName);
        }
    }

    #endregion
}