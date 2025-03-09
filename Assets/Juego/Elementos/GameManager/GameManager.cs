using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using TMPro;
using System.Linq;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Rondas")]
    [SyncVar(hook = nameof(OnTimerChanged))] private float currentDecisionTime;
    [SerializeField] private float decisionTime = 10f; // Tiempo para elegir acción
    [SerializeField] private float executionTime = 4f; // Tiempo para mostrar resultados
    [SerializeField] private TMP_Text timerText;

    private bool isDecisionPhase = true;
    [SerializeField] private List<PlayerController> players = new List<PlayerController>();
    private Dictionary<PlayerController, PlayerAction> actionsQueue = new Dictionary<PlayerController, PlayerAction>();

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
            StartCoroutine(RoundCycle()); // Solo el servidor inicia el ciclo de rondas
        }

    }

    private IEnumerator RoundCycle()
    {
        while (true)
        {
            yield return StartCoroutine(DecisionPhase());
            yield return StartCoroutine(ExecutionPhase());
            ResetAllCovers(); //Elimina coberturas
        }
    }

    private IEnumerator DecisionPhase()
    {
        isDecisionPhase = true;

        foreach (var player in players)
        {
            player.TargetPlayButtonAnimation(player.connectionToClient, "Venir", true);
            player.GetComponent<NetworkAnimator>().animator.Play("Idle");
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

            if(!actionsQueue.ContainsKey(player)) // Si no se eligió acción alguna se llama a None
    {
                actionsQueue[player] = new PlayerAction(ActionType.None);
                Debug.Log($"[GameManager] {player.gameObject.name} no eligió ninguna acción, registrando 'None'.");
                player.RpcSendLogToClients($"{player.gameObject.name} no eligió ninguna acción, registrando 'None'.");
            }
        }

        yield return new WaitForSeconds(0.5f);//Tiempo para que se ejecute la animación
    }

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
                float probability = entry.Key.coverProbabilities[Mathf.Min(entry.Key.consecutiveCovers, entry.Key.coverProbabilities.Length - 1)];

                if (Random.value <= probability)
                {
                    entry.Key.isCovering = true; //Manejado por el serivdor
                    entry.Key.RpcUpdateCover(true);
                    entry.Key.GetComponent<NetworkAnimator>().animator.Play("Cover");
                    entry.Key.consecutiveCovers++;
                    Debug.Log($"[SERVER] {entry.Key.gameObject.name} se cubrió con éxito en el intento { entry.Key.consecutiveCovers + 1}");
                    entry.Key.RpcSendLogToClients($"{entry.Key.gameObject.name} se cubrió con éxito en el intento {entry.Key.consecutiveCovers + 1}");
                }
                else
                {
                    entry.Key.GetComponent<NetworkAnimator>().animator.Play("CoverFail");
                    Debug.Log($"[SERVER] {entry.Key.gameObject.name} intento cubrirse, pero falló en el intento {entry.Key.consecutiveCovers + 1}");
                    entry.Key.RpcSendLogToClients($"{entry.Key.gameObject.name} intento cubrirse, pero falló en el intento {entry.Key.consecutiveCovers + 1}");
                }
                //El servidor envía la actualizacion de UI a cada cliente
                float updatedProbability = entry.Key.coverProbabilities[Mathf.Min(entry.Key.consecutiveCovers, entry.Key.coverProbabilities.Length - 1)];
                entry.Key.RpcUpdateCoverProbabilityUI(updatedProbability); //Actualizar UI de probabilidad de cubrirse
            }
        }

        yield return new WaitForSeconds(0.5f); //Pausa antes del tiroteo
                
        foreach (var entry in actionsQueue) //Luego aplica "Disparar" y "Recargar"
        {
            switch (entry.Value.type)
            {
                case ActionType.Reload:
                    entry.Key.ServerReload();
                    entry.Key.GetComponent<NetworkAnimator>().animator.Play("Reload");
                    entry.Key.consecutiveCovers = 0; //Reinicia la posibilidad de cobertura al máximo otra ves
                    entry.Key.RpcUpdateCoverProbabilityUI(entry.Key.coverProbabilities[0]); //Actualizar UI de probabilidad de cubrirse
                    break;
                case ActionType.Shoot:
                    entry.Key.ServerAttemptShoot(entry.Value.target);
                    entry.Key.GetComponent<NetworkAnimator>().animator.Play("Shoot");
                    entry.Key.consecutiveCovers = 0; //Reinicia la posibilidad de cobertura al máximo otra ves
                    entry.Key.RpcUpdateCoverProbabilityUI(entry.Key.coverProbabilities[0]); //Actualizar UI de probabilidad de cubrirse
                    break;
                case ActionType.SuperShoot:
                    entry.Key.ServerAttemptShoot(entry.Value.target);
                    entry.Key.GetComponent<NetworkAnimator>().animator.Play("SuperShoot");
                    entry.Key.consecutiveCovers = 0; //Reinicia la posibilidad de cobertura al máximo otra ves
                    entry.Key.RpcUpdateCoverProbabilityUI(entry.Key.coverProbabilities[0]); //Actualizar UI de probabilidad de cubrirse
                    break;
                case ActionType.None:
                    entry.Key.GetComponent<NetworkAnimator>().animator.Play("None");
                    break;
            }
        }

        foreach (var player in players)
        {
            player.selectedAction = ActionType.None;
            player.TargetUpdateUI(player.connectionToClient, player.ammo);

            // Mostrar la cuenta regresiva en todos los clientes
            player.RpcShowCountdown(executionTime);
        }

        yield return new WaitForSeconds(executionTime);

        CheckGameOver();
        currentDecisionTime = decisionTime; //Devolver el valor anterior del timer
    }

    public void SetPause(bool pauseState)
    {
        Time.timeScale = pauseState ? 0f : 1f; //Si la consola está abierta, pausamos; si está cerrada, despausamos.

        Debug.Log($"[GameManager] Pausa: {(pauseState ? "Activada" : "Desactivada")}");
    }

    #region Game Management

    private void CheckGameOver()
    {
        //Contar número de jugadores vivos
        int alivePlayers = players.Count(player => player.isAlive);

        //Si no queda nadie vivo, la partida se detiene
        if (alivePlayers == 0)
        {
            Debug.Log("Todos los jugadores han muerto. Que montón de inútiles hahahaha");
            StopAllCoroutines();
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

            StopAllCoroutines();
        }
    }

    public bool IsDecisionPhase()
    {
        return isDecisionPhase; //Esta función sirve para que PlayerController pueda saber cuando es true DecisionPhase(), así saber cuando meter variables por ejemplo en el UpdateUI();
    }

    public void RegisterAction(PlayerController player, ActionType actionType, PlayerController target = null)
    {
        if (!isDecisionPhase) return; //Solo se pueden elegir acciones en la fase de decisión

        actionsQueue[player] = new PlayerAction(actionType, target);
        Debug.Log($"{player.gameObject.name} ha elegido {actionType}");
        player.RpcSendLogToClients($"{player.gameObject.name} ha elegido {actionType}");

        if (actionType == ActionType.SuperShoot)
        {
            Debug.Log($"{player.gameObject.name} ha elegido SUPER SHOOT contra {target.gameObject.name}");
            player.RpcSendLogToClients($"{player.gameObject.name} ha elegido SUPER SHOOT contra {target.gameObject.name}");
        }

        if (actionType == ActionType.Shoot)
        {
            Debug.Log($"{player.gameObject.name} ha elegido SHOOT contra {target.gameObject.name}");
            player.RpcSendLogToClients($"{player.gameObject.name} ha elegido SHOOT contra {target.gameObject.name}");
        }
    }

    public void RegisterPlayer(PlayerController player)
    {
        if (!players.Contains(player))
            players.Add(player);
    }

    #endregion

    #region SERVER
    [Server]
    public void PlayerDied(PlayerController deadPlayer)
    {
        if (!isServer) return;

        players.Remove(deadPlayer);

        CheckGameOver();
    }


    [Server]
    private void ResetAllCovers()
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

    [Server]
    public void ServerRegisterShoot(PlayerController shooter, PlayerController target)
    {
        if (shooter == null || target == null) return;

        //En lugar de disparar de inmediato, solo guardamos la acción en la cola
        RegisterAction(shooter, ActionType.Shoot, target);
    }

    #endregion

    #region Victoria/Derrota

    #endregion

    #region UI HOOKS

    private void OnTimerChanged(float oldTime, float newTime)
    {
        if (timerText != null)
        {
            timerText.text = $"Tiempo: {newTime}";
            timerText.gameObject.SetActive(newTime > 0);  // 🔹 Se oculta automáticamente cuando es 0
        }
    }

    #endregion
}