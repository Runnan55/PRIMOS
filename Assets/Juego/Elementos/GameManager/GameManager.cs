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
    [SerializeField] private float roundDuration = 10f;
    private Dictionary<PlayerController, PlayerAction> playerActions = new Dictionary<PlayerController, PlayerAction>();

    private void Awake()
    {
        if(Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }

    // Registrar a los jugadores
    [Server]
    public void RegisterPlayer(PlayerController player)
    {   
        if(!players.Contains(player))
        {
            players.Add(player);
        }

        if(players.Count <= 0)
        {
            Debug.Log("Todos perdieron");
        }

        if(players.Count == 1)
        {
            DeclareWinner();
            return;
        }

        if(players.Count >= 2)
        {
            StartCoroutine(RoundCycle());
        }
    }

    //
    [Server]
    IEnumerator RoundCycle()
    {
        while(players.Count(p => p.isAlive) > 1)
        {
            playerActions.Clear();
            RpcStartRound();

            yield return new WaitForSeconds(roundDuration);

            ProcessActions();

            yield return new WaitForSeconds(1f);
        }

        DeclareWinner();
    }

    [Server]
    public void SubmitAction(PlayerController player, PlayerAction action)
    {
        if(players.Contains(player) && player.isAlive)
        {
            playerActions[player] = action;
        }
    }

    [Server]
    void ProcessActions()
    {
        foreach(var entry in playerActions)
        {
            PlayerController player = entry.Key;
            PlayerAction action = entry.Value;

            switch (action.type)
            {
                case ActionType.Shoot:
                    player.AttemptShoot(action.target);
                    break;
                case ActionType.Reload:
                    player.Reload();
                    break;
                case ActionType.Cover:
                    player.Cover();
                    break;
            }
        }
    }

    [Server]
    void DeclareWinner()
    {
        PlayerController winner = players.FirstOrDefault(p => p.isAlive);
        if(winner != null)
        {
            winner.RpcDeclareVictory();
        }
    }

    [ClientRpc]
    void RpcStartRound()
    {
        foreach(var player in players)
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

