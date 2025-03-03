using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using TMPro;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Rondas")]
    [SyncVar(hook = nameof(OnTimerChanged))]private float currentDecisionTime;
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
        while(true)
        {
            yield return StartCoroutine(DecisionPhase());
            yield return StartCoroutine(ExecutionPhase());
            ResetAllCovers(); //Elimina coberturas
        }
    }

    private IEnumerator DecisionPhase()
    {
        isDecisionPhase = true;
        actionsQueue.Clear();

        currentDecisionTime = decisionTime; // 🔹 Restaurar el tiempo original
        timerText.gameObject.SetActive(true);

        Debug.Log("Comienza la fase de decisión. Elige rápido wey");

        while (currentDecisionTime > 0)
        {
            yield return new WaitForSeconds(1f);
            currentDecisionTime = Mathf.Max(0, currentDecisionTime - 1);
        }
        Debug.Log("Finalizó el tiempo de decisión.");
    }

    private IEnumerator ExecutionPhase()
    {
        isDecisionPhase = false;
        currentDecisionTime = 0;

        //Prioridad a la accion cubrirse
        foreach (var entry in actionsQueue)
        {
            if (entry.Value.type == ActionType.Cover)
                entry.Key.isCovering = true; // Ahora el servidor lo maneja directamente
                entry.Key.RpcUpdateCover(true);
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
        currentDecisionTime = decisionTime; //Devolver el valor anterior del timer
    }

    #region Game Management

   /* private void CheckGameOver()
    {
        players.RemoveAll(p => !p.isAlive); //Filtramos solo jugadores vivos

        if (players.Count == 1) //Solo queda un jugador vivo
        {
            Debug.Log($"🎉 ¡{players[0].gameObject.name} ha ganado la partida!");
            RpcShowVictory(players[0].netId);
        }
        else if ( players.Count <= 0)
        {
            Debug.Log("Todos han muerto");
        }
    }*/

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

        if (players.Count == 1) // Si solo queda un jugador vivo, es el ganador
        {
            players[0].RpcOnVictory();
        }
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
   /* [Server]
    public void PlayerDied(PlayerController player)
    {
        if (player == null) return;

        Debug.Log($"{player.gameObject.name} ha muerto.");
        RpcShowDefeat(player.netId);
    }

    [Server]
    public void PlayerWon(PlayerController player)
    {
        if (player == null) return;

        Debug.Log($"🎉 {player.gameObject.name} ha ganado.");
        RpcShowVictory(player.netId);
    }*/
/*
    [ClientRpc]
    void RpcShowVictory(uint playerId)
    {
        foreach (var player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (player.netId == playerId && player.isLocalPlayer)
            {
                player.ShowVictoryUI();
            }
        }
    }

    [ClientRpc]
    void RpcShowDefeat(uint playerId)
    {
        foreach (var player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (player.netId == playerId && player.isLocalPlayer)
            {
                player.ShowDefeatUI();
            }
        }
    }
    */
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