using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using TMPro;
using System.Linq;
using System;
using Mirror.BouncyCastle.Security;
using Unity.VisualScripting;

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
    [SyncVar(hook = nameof(OnTimerChanged))] private float currentDecisionTime;
    [SerializeField] private float decisionTime = 10f; // Tiempo para elegir acción
    [SerializeField] private float executionTime = 4f; // Tiempo para mostrar resultados
    [SerializeField] private TMP_Text timerText;

    private bool isDecisionPhase = true;
    private bool isGameOver = false;
    private Coroutine roundCycleCoroutine;
    private Coroutine decisionPhaseCoroutine;
    private Coroutine executionPhaseCoroutine;

    [SerializeField] private List<PlayerController> players = new List<PlayerController>(); //Lista de jugadores que entran
    [SerializeField] private List<PlayerController> deadPlayers = new List<PlayerController>(); //Lista de jugadores muertos
    private HashSet<PlayerController> damagedPlayers = new HashSet<PlayerController>(); // Para almacenar jugadores que ya recibieron daño en la ronda

    private Dictionary<PlayerController, PlayerAction> actionsQueue = new Dictionary<PlayerController, PlayerAction>();


    [SyncVar] private int currentRound = 0; // Contador de rondas
    [SerializeField] private TMP_Text roundText; // Texto en UI para mostrar la ronda

    [Header("Draw")]
    private bool isDraw = false;


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

    [Header("Talisman-Tiki")]
    [SerializeField] private GameObject talismanIconPrefab;
    private GameObject talismanIconInstance;
    [SerializeField] private Vector3 talismanOffset = new Vector3(0, 1.5f, 0);
    [SerializeField] private float talismanMoveDuration = 0.75f;

    private PlayerController talismanHolder;
    [SyncVar] private uint talismanHolderNetId;

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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }

    private void Start()
    {
        if (isServer)
        {
            talismanHolder = players.FirstOrDefault(p => p.isAlive);
            if (talismanHolder != null)
            {
                talismanHolderNetId = talismanHolder.netId;
                RpcSpawnTalisman(talismanHolderNetId);
            }
        }
    }

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
        if (!isServer) return;

        if (gameStatistic != null)
        {
            gameStatistic.Initialize(players);
        }

        foreach (var player in players)
        {
            if (!player.hasConfirmedName)
            {
                Debug.Log("No todos los jugadores han ingresado su nombre");
                return;
            }
        }

        if (randomizeModifier)
        {
            ChooseRandomModifier();
        }

        Debug.Log($"[GameManager] Modificador seleccionado: {SelectedModifier}");
        ApplyGameModifier(SelectedModifier);

        Debug.Log("Todos los jugadores han ingresado su nombre. ¡La partida puede comenzar!");
        roundCycleCoroutine = StartCoroutine(RoundCycle());

        talismanHolder = players.FirstOrDefault(p => p.isAlive); // El primero con vida
        Debug.Log($"[Talisman] Se asignó el talismán a {talismanHolder.playerName}");

        if (talismanHolder != null)
        {
            talismanHolderNetId = talismanHolder.netId;
            RpcSpawnTalisman(talismanHolder.netId);
        }

    }

    public void RegisterPlayer(PlayerController player)//Registra a cada jugador usando OnStartServer() en PlayerController
    {
        if (!players.Contains(player))
            players.Add(player);
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

    [ClientRpc]
    public void RpcUpdateRoundUI(int roundNumber)
    {
        if (roundText != null)
            roundText.text = $"Ronda: {roundNumber}";
    }

    #region MisionRapida
    [Server]
    private void AssignRandomQuickMission(PlayerController player)
    {
        QuickMissionType type = (QuickMissionType) UnityEngine.Random.Range(0,Enum.GetValues(typeof(QuickMissionType)).Length);
        player.currentQuickMission = new QuickMission(type, currentRound);

        string animName = "QM_Start_" + player.currentQuickMission.type.ToString(); // El nombre exacto de la misión como string
        player.TargetPlayAnimation(animName);
    }

    private bool EvaluateQuickMission(PlayerController player, QuickMission mission)
    {
        switch (mission.type)
        {
            case QuickMissionType.DealDamage:
                return player.lastShotTarget != null;

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
                player.shieldBoostActivate = true;
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
        isDecisionPhase = true;
        currentRound++;
        RpcUpdateRoundUI(currentRound);

        foreach (var player in players)
        {
            player.canDealDamageThisRound = true;
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

        // Mostrar en pantalla la ronda actual
        if (roundText != null)
            roundText.text = $"Ronda: {currentRound}";

        foreach (var player in players)
        {
            player.TargetPlayButtonAnimation(player.connectionToClient, "Venir", true);
            player.RpcPlayAnimation("Idle");
        }

        actionsQueue.Clear();

        currentDecisionTime = decisionTime; //Restaurar el tiempo original
        timerText.gameObject.SetActive(true);

        Debug.Log("Comienza la fase de decisión. Jugadores decidiendo acciones");

        while (currentDecisionTime > 0)
        {
            yield return new WaitForSeconds(1f);
            currentDecisionTime = Mathf.Max(0, currentDecisionTime - 1);
        }

        isDecisionPhase = false;
        Debug.Log("Finalizó el tiempo de decisión.");

        foreach (var player in players)
        {
            player.TargetPlayButtonAnimation(player.connectionToClient, "Irse", false);
            player.RpcCancelAiming();

            if (player.currentQuickMission == null)
            {
                player.TargetPlayAnimation("EmptyState");
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
                    entry.Key.RpcPlayAnimation("Cover");
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

        foreach (var target in targetToShooters.Keys)
        {
            List<PlayerController> shooters = targetToShooters[target];

            if (shooters.Count == 1)
            {
                //shooters[0].ServerAttemptShoot(target);
            }
            else
            {
                PlayerController chosenShooter = GetClosestToTalisman(shooters);
                //chosenShooter.ServerAttemptShoot(target);

                Debug.Log($"[Talisman] {chosenShooter.playerName} gana la prioridad para atacar a {target.playerName}"); //Los demás fallan el tiro pero no se muestra

                foreach (var shooter in shooters)
                {
                    shooter.canDealDamageThisRound = (shooter == chosenShooter);
                }
            }
        }

        yield return new WaitForSeconds(0.5f); //Pausa antes del tiroteo
                
        foreach (var entry in actionsQueue) //Luego aplica "Disparar" y "Recargar"
        {
            switch (entry.Value.type)
            {
                case ActionType.Reload:
                    entry.Key.ServerReload();
                    entry.Key.consecutiveCovers = 0; //Reinicia la posibilidad de cobertura al máximo otra ves
                    entry.Key.RpcUpdateCoverProbabilityUI(entry.Key.coverProbabilities[0]); //Actualizar UI de probabilidad de cubrirse
                    break;
                case ActionType.Shoot:
                    entry.Key.ServerAttemptShoot(entry.Value.target);
                    entry.Key.consecutiveCovers = 0; //Reinicia la posibilidad de cobertura al máximo otra ves
                    entry.Key.RpcUpdateCoverProbabilityUI(entry.Key.coverProbabilities[0]); //Actualizar UI de probabilidad de cubrirse
                    break;
                case ActionType.SuperShoot:
                    entry.Key.ServerAttemptShoot(entry.Value.target);
                    entry.Key.consecutiveCovers = 0; //Reinicia la posibilidad de cobertura al máximo otra ves
                    entry.Key.RpcUpdateCoverProbabilityUI(entry.Key.coverProbabilities[0]); //Actualizar UI de probabilidad de cubrirse
                    break;
                case ActionType.None:
                    entry.Key.RpcPlayAnimation("None");
                    break;
            }
        }

        foreach (var player in players)
        {
            player.selectedAction = ActionType.None;

            var mission = player.currentQuickMission;

            if (mission != null && mission.assignedRound == currentRound)
            {
                bool success = EvaluateQuickMission(player, mission);

                if (success)
                {
                    ApplyQuickMissionReward(mission.type, player);
                    player.RpcSendLogToClients($"{player.playerName}¡Completaste tu misión rápida!");
                }
                else
                {
                    player.RpcSendLogToClients($"{player.playerName} Fallaste tu misión rápida.");
                }

                // SIEMPRE limpiamos la misión al final de la ronda
                player.currentQuickMission = null;

                string animSuffix = success ? "Reward" : "Exit";
                string animName = $"QM_{animSuffix}_{mission.type}";
                player.TargetPlayAnimation(animName);
            }
        }
        AdvanceTalisman();

        damagedPlayers.Clear(); // Permite recibir daño en la siguiente ronda

        CheckGameOver();

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

    [ClientRpc]
    private void RpcSpawnTalisman(uint holderNetId)
    {
        if (!NetworkClient.spawned.TryGetValue(holderNetId, out NetworkIdentity identity))
            return;

        PlayerController targetPlayer = identity.GetComponent<PlayerController>();
        if (targetPlayer == null || talismanIconPrefab == null) return;

        if (talismanIconInstance == null)
        {
            talismanIconInstance = Instantiate(talismanIconPrefab);
        }

        talismanIconInstance.transform.position = targetPlayer.transform.position + talismanOffset;
    }

    [Server]
    private void AdvanceTalisman()
    {
        if (players.Count == 0) return;

        int currentIndex = players.IndexOf(talismanHolder);

        for (int i = 1; i <= players.Count; i++)
        {
            int nextIndex = (currentIndex + i) % players.Count;
            if (players[nextIndex].isAlive)
            {
                PlayerController previousHolder = talismanHolder;
                talismanHolder = players[nextIndex];
                talismanHolderNetId = talismanHolder.netId;

                RpcMoveTalisman(previousHolder.netId, talismanHolder.netId);
                Debug.Log($"[Talisman] Ahora lo tiene {talismanHolder.playerName}");
                break;
            }
        }
    }

    [ClientRpc]
    private void RpcMoveTalisman(uint fromNetId, uint toNetId)
    {
        NetworkIdentity fromIdentity = NetworkClient.spawned[fromNetId];
        NetworkIdentity toIdentity = NetworkClient.spawned[toNetId];

        if (fromIdentity == null || toIdentity == null || talismanIconInstance == null) return;

        Vector3 start = fromIdentity.transform.position + talismanOffset;
        Vector3 end = toIdentity.transform.position + talismanOffset;

        StopAllCoroutines();
        StartCoroutine(MoveTalismanVisual(start, end));
    }

    private IEnumerator MoveTalismanVisual(Vector3 start, Vector3 end)
    {
        float elapsed = 0f;
        Vector3 peak = (start + end) / 2 + Vector3.up * 1.5f;

        while (elapsed < talismanMoveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / talismanMoveDuration);
            Vector3 pos = Mathf.Pow(1 - t, 2) * start + 2 * (1 - t) * t * peak + Mathf.Pow(t, 2) * end;
            talismanIconInstance.transform.position = pos;
            yield return null;
        }

        talismanIconInstance.transform.position = end;
    }

    private PlayerController GetClosestToTalisman(List<PlayerController> shooters)
    {
        int talismanIndex = players.IndexOf(talismanHolder);

        int minSteps = players.Count;
        PlayerController chosen = null;

        foreach (var shooter in shooters)
        {
            int shooterIndex = players.IndexOf(shooter);
            int steps = (shooterIndex - talismanIndex + players.Count) % players.Count;

            if (steps < minSteps)
            {
                minSteps = steps;
                chosen = shooter;
            }
        }

        return chosen;
    }

    private void CheckGameOver()
    {
        //Contar número de jugadores vivos
        int alivePlayers = players.Count(player => player.isAlive);

        //Si no queda nadie vivo, la partida se detiene
        if (alivePlayers == 0)
        {
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

        // Agregar un delay antes de procesar la muerte
        StartCoroutine(HandlePlayerDeath(deadPlayer));

        PlayerController killer = players.FirstOrDefault(p => p.lastShotTarget == deadPlayer);

        if (killer != null && RolesManager.Instance != null)
        {
            RolesManager.Instance.TransferParcaRole(killer, deadPlayer);
        }
    }

    private IEnumerator HandlePlayerDeath(PlayerController deadPlayer)
    {
        yield return new WaitForSeconds(0.1f); // Pequeño delay para registrar todas las acciones antes de procesar la muerte
        players.Remove(deadPlayer);
        deadPlayers.Add(deadPlayer); // Movemos al jugador del grupo de vivos a muertos

        yield return new WaitForSeconds(0.1f);//Otro pequeño delay para permitir el registro de muertes antes del CheckGameOver()

        bool wasDraw = false;

        CheckGameOver();
        wasDraw = isDraw;

        if (!wasDraw)
        {
            deadPlayer.RpcOnDeath();
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
        if (!isDecisionPhase || !player.isAlive) return; //Solo se pueden elegir acciones en la fase de decisión ni si estás muerto

        actionsQueue[player] = new PlayerAction(actionType, target);
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

    #region UI HOOKS (Time)

    private void OnTimerChanged(float oldTime, float newTime) //Control de timer de Decision
    {
        if (timerText != null)
        {
            int displayTime = Mathf.FloorToInt(newTime - 1.5f);

            if(newTime > 0f && displayTime >= 0)
            {
                timerText.text = $"Tiempo: {displayTime}";
                timerText.gameObject.SetActive(true);
            }
            else
            {
                timerText.gameObject.SetActive(false); // Ocultar cuando termina
            }

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

        CheckGameOver();
    }

    #endregion
}