using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; 


public class GameManager : MonoBehaviour
{
    public List<JugadorController> jugadores = new List<JugadorController>();
    public TurnoManager turnoManager;
    private bool turnoEnCurso = false;
    


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
}
