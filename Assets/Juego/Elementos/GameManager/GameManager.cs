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
    [SerializeField] private float executionTime = 3f; // Tiempo para mostrar resultados
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
        foreach (var player in players)
        {
            player.TargetPlayButtonAnimation(player.connectionToClient, "Venir", true);
            player.GetComponent<NetworkAnimator>().animator.Play("Idle");
            player.UpdateUI();
        }

        isDecisionPhase = true;
        actionsQueue.Clear();

        currentDecisionTime = decisionTime; //Restaurar el tiempo original
        timerText.gameObject.SetActive(true);

        Debug.Log("Comienza la fase de decisión. Jugadores decidiendo acciones");

        while (currentDecisionTime > 0)
        {
            yield return new WaitForSeconds(1f);
            currentDecisionTime = Mathf.Max(0, currentDecisionTime - 1);
        }
        Debug.Log("Finalizó el tiempo de decisión.");

        foreach (var player in players)
        {
            player.TargetPlayButtonAnimation(player.connectionToClient, "Irse", false);
            player.RpcCancelAiming();
        }
    }

    private IEnumerator ExecutionPhase()
    {
        isDecisionPhase = false;
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
                entry.Key.isCovering = true; //Manejado por el serivdor
                entry.Key.RpcUpdateCover(true);
                entry.Key.GetComponent<NetworkAnimator>().animator.Play("Cover");
            }
        }

        yield return new WaitForSeconds(0.1f); //Pausa antes del tiroteo

        //SuperShoot
        foreach (var entry in actionsQueue)
        {
            if (entry.Value.type == ActionType.SuperShoot)
            {
                PlayerController shooter = entry.Key;
                PlayerController target = entry.Value.target;

                if (shooter.ammo > 3) // Verificar que aún tenga balas suficientes
                {
                    shooter.ammo -= 3; // Consume 3 balas

                    shooter.ServerAttemptShoot(target); // Ahora dispara normal
                    shooter.GetComponent<NetworkAnimator>().animator.Play("Shoot");
                }
            }
        }

        //Luego aplica "Disparar" y "Recargar"
        foreach (var entry in actionsQueue)
        {
            switch (entry.Value.type)
            {
                case ActionType.Reload:
                    entry.Key.ServerReload();
                    entry.Key.GetComponent<NetworkAnimator>().animator.Play("Reload");
                    break;
                case ActionType.Shoot:
                    entry.Key.ServerAttemptShoot(entry.Value.target);
                    entry.Key.GetComponent<NetworkAnimator>().animator.Play("Shoot");
                    break;
            }
        }

        yield return new WaitForSeconds(executionTime);
        CheckGameOver();
        currentDecisionTime = decisionTime; //Devolver el valor anterior del timer
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
                Debug.Log($"{winner.gameObject.name} ha ganado la partida");
                winner.RpcOnVictory();
            }

            StopAllCoroutines();
        }
    }


    public void RegisterAction(PlayerController player, ActionType actionType, PlayerController target = null)
    {
        if (!isDecisionPhase) return; //Solo se pueden elegir acciones en la fase de decisión

        actionsQueue[player] = new PlayerAction(actionType, target);
        Debug.Log($"{player.gameObject.name} ha elegido {actionType}");
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