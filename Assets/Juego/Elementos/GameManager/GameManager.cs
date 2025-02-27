using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using UnityEngine.InputSystem;
using System.Collections;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }
    [SerializeField] private List<PlayerController> players = new List<PlayerController>();

    [SerializeField] private float roundDuration = 10f; // Tiempo para elegir acción
    [SerializeField] private float inactivityTime = 3f; // Tiempo para mostrar resultados

    [SerializeField] private Button btnDisparar, btnCubrirse, btnRecargar; // Botones UI

    private Dictionary<PlayerController, PlayerAction> playerActions = new Dictionary<PlayerController, PlayerAction>();
    private PlayerController localPlayer; // Referencia al jugador local

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
        btnDisparar.onClick.AddListener(() => SelectAction(ActionType.Shoot));
        btnCubrirse.onClick.AddListener(() => SelectAction(ActionType.Cover));
        btnRecargar.onClick.AddListener(() => SelectAction(ActionType.Reload));
        if (isServer)
        {
            StartCoroutine(RoundCycle()); // Solo el servidor inicia el ciclo de rondas
        }

    }

    public void SetLocalPlayer(PlayerController player)
    {
        localPlayer = player;
    }

    public void SelectAction(ActionType actionType)
    {
        if (localPlayer == null || !localPlayer.isAlive) return;

        switch (actionType)
        {
            case ActionType.Shoot:
                Debug.Log("Selecciona un objetivo para disparar.");
                localPlayer.selectedAction = ActionType.Shoot;
                break;
            case ActionType.Reload:
                SubmitAction(localPlayer, new PlayerAction(ActionType.Reload));
                break;
            case ActionType.Cover:
                SubmitAction(localPlayer, new PlayerAction(ActionType.Cover));
                break;
        }
    }

    public void SelectTarget(PlayerController target)
    {
        if (localPlayer == null || !localPlayer.isAlive) return;

        if (localPlayer.selectedAction == ActionType.Shoot && target != localPlayer && target.isAlive)
        {
            SubmitAction(localPlayer, new PlayerAction(ActionType.Shoot, target));
            localPlayer.selectedAction = ActionType.None;
        }
    }

    [Server]
    public void SubmitAction(PlayerController player, PlayerAction action)
    {
        if (!playerActions.ContainsKey(player) && players.Contains(player) && player.isAlive)
        {
            playerActions[player] = action;
            Debug.Log($"{player.gameObject.name} seleccionó: {action.type}");
        }
    }

    [Server]
    IEnumerator RoundCycle()
    {
        while (players.Count(p => p.isAlive) > 1)
        {
            playerActions.Clear();
            RpcStartRound();
            for (float t = roundDuration; t > 0; t -= 1f)
            {
                Debug.Log($"Tiempo restante de ronda: {t} segundos");
                yield return new WaitForSeconds(1f);
            }
            ProcessActions();
            for (float t = inactivityTime; t > 0; t -= 1f)
            {
                Debug.Log($"Tiempo de inactividad restante: {t} segundos");
                yield return new WaitForSeconds(1f);
            }
        }
        DeclareWinner();
    }

    [Server]
    void ProcessActions()
    {
        // 1. Procesar coberturas primero
        foreach (var entry in playerActions.Where(a => a.Value.type == ActionType.Cover))
        {
            entry.Key.Cover();
        }

        // 2. Procesar recargas
        foreach (var entry in playerActions.Where(a => a.Value.type == ActionType.Reload))
        {
            entry.Key.Reload();
        }

        // 3. Procesar disparos al final (después de coberturas y recargas)
        foreach (var entry in playerActions.Where(a => a.Value.type == ActionType.Shoot))
        {
            entry.Key.AttemptShoot(entry.Value.target);
        }
    }

    [Server]
    void DeclareWinner()
    {
        PlayerController winner = players.FirstOrDefault(p => p.isAlive);
        if (winner != null)
        {
            winner.RpcDeclareVictory();
        }
    }

    [ClientRpc]
    void RpcStartRound()
    {
        foreach (var player in players)
        {
            player.StartTurn();
        }
    }
}


/*
void Start()
{
    AsignarJugadores();
    IniciarTurno();
}

void AsignarJugadores()
{
    JugadorController[] jugadoresEncontrados = FindObjectsOfType<JugadorController>();
    jugadores.Clear();

    int idAsignado = 1;
    foreach (var jugador in jugadoresEncontrados)
    {
        if (idAsignado <= 6)
        {
            jugador.id = idAsignado;
            jugadores.Add(jugador);
            idAsignado++;

            // Asignar evento de selección a cada jugador
            Button botonSeleccion = jugador.GetComponentInChildren<Button>();
            if (botonSeleccion != null)
            {
                int idObjetivo = jugador.id; // Evita problemas de referencias
                botonSeleccion.onClick.AddListener(() => SeleccionarObjetivo(idObjetivo));
            }
        }
    }
}

public void SeleccionarObjetivo(int idObjetivo)
{
    JugadorController jugadorSeleccionado = jugadores.Find(j => j.id == idObjetivo);
    if (jugadorSeleccionado != null)
    {
        foreach (var jugador in jugadores)
        {
            if (jugador.id != idObjetivo) 
            {
                jugador.SeleccionarObjetivo(jugadorSeleccionado);
            }
        }
    }
}

public void IniciarTurno()
{
    turnoEnCurso = true;
    turnoManager.IniciarTurno();
}

void ResolverTurno()
{
    foreach (var jugador in jugadores)
    {
        if (jugador.accionSeleccionada == "Disparar" && jugador.objetivo != null)
        {
            if (!jugador.objetivo.cubierto)
            {
                jugador.objetivo.vidas--;
                Debug.Log($"Jugador {jugador.id} disparó a {jugador.objetivo.id}. Vidas restantes: {jugador.objetivo.vidas}");
            }
            else
            {
                Debug.Log($"Jugador {jugador.id} intentó disparar a {jugador.objetivo.id}, pero estaba cubierto.");
            }
        }
    }

    jugadores.RemoveAll(j => j.vidas <= 0);
    if (jugadores.Count == 1) FinDelJuego(jugadores[0]);

    foreach (var jugador in jugadores) jugador.cubierto = false;
}

void FinDelJuego(JugadorController ganador)
{
    Debug.Log("¡Ganador: " + ganador.id + "!");
}
*/

