using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using TMPro;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Rondas")]
    [SerializeField] private float decisionTime = 10f; // Tiempo para elegir acción
    private float currentDecisionTime;
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
        while(true)
        {
            yield return StartCoroutine(DecisionPhase());
            yield return StartCoroutine(ExecutionPhase());

            CheckGameOver();
        }
    }

    private IEnumerator DecisionPhase()
    {
        isDecisionPhase = true;
        actionsQueue.Clear();

        currentDecisionTime = decisionTime; // 🔹 Restaurar el tiempo original
        SetTimer(currentDecisionTime);
        timerText.gameObject.SetActive(true);

        Debug.Log("Comienza la fase de decisión. Elige rápido wey");

        while (currentDecisionTime > 0)
        {
            UpdateTimerUI();
            yield return new WaitForSeconds(1f);
            currentDecisionTime--;
        }
        UpdateTimerUI();
        Debug.Log("Finalizó el tiempo de decisión.");
    }

    private IEnumerator ExecutionPhase()
    {
        isDecisionPhase = false;

        timerText.gameObject.SetActive(false);
        RpcUpdateTimer(0); // 🔹 Ocultar el tiempo en todos los clientes

        //Prioridad a la accion cubrirse
        foreach (var entry in actionsQueue)
        {
            if (entry.Value.type == ActionType.Cover)
                entry.Key.CmdCover();
        }

        yield return new WaitForSeconds(0.5f); //Pausa antes del tiroteo

        //Luego aplica "Disparar" y "Recargar"
        foreach(var entry in actionsQueue)
        {
            switch(entry.Value.type)
            {
                case ActionType.Reload:
                    entry.Key.ServerReload();
                    break;
                case ActionType.Shoot:
                    entry.Key.ServerAttemptShoot(entry.Value.target);
                    break;
            }
        }

        yield return new WaitForSeconds(executionTime);

        //Hace que los jugadores dejen de cubrirse al finalizar la fase de ejecución ;)
        Debug.Log(" Reseteando cobertura de todos los jugadores...");
        ResetAllCovers();

        timerText.gameObject.SetActive(true);
        RpcUpdateTimer(currentDecisionTime); //Reactivar el timer en los clientes
    }

    private void CheckGameOver()
    {
        players.RemoveAll(p => !p.isAlive); //Filtramos solo jugadores vivos

        if (players.Count == 1) //Solo queda un jugador vivo
        {
            Debug.Log($"🎉 ¡{players[0].gameObject.name} ha ganado la partida!");
            players[0].RpcDeclareVictory();
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

    [Server]
    private void ResetAllCovers()
    {
        Debug.Log("[SERVER] Reseteando cobertura de todos los jugadores...");

        foreach (var player in players)
        {
            if (player != null)
            {
                Debug.Log($"[SERVER] Resetting cover for {player.gameObject.name}");
                player.RpcCoveringOff();
            }
        }
    }

    [Server]
    public void ServerRegisterShoot(PlayerController shooter, PlayerController target)
    {
        if (shooter == null || target == null) return;

        Debug.Log($"{shooter.gameObject.name} quiere disparar a {target.gameObject.name}");

        // 🔹 En lugar de disparar de inmediato, solo guardamos la acción en la cola
        RegisterAction(shooter, ActionType.Shoot, target);
    }

    private void UpdateTimerUI()
    {
        if (timerText != null)
            timerText.text = $"Tiempo: {currentDecisionTime}";

        RpcUpdateTimer(currentDecisionTime); // 🔹 Enviar a los clientes
    }

    [ClientRpc]
    private void RpcUpdateTimer(float time)
    {
        if (timerText != null)
            timerText.text = $"Tiempo: {time}";
    }

    private void SetTimer(float time)
    {
        decisionTime = time;
        UpdateTimerUI();
    }
}