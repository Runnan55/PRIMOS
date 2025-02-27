using UnityEngine;
using UnityEngine.UI;

public class JugadorController : MonoBehaviour
{
    public int id;
    public int vidas = 3;
    public int balas = 1;
    public bool cubierto = false;
    public string accionSeleccionada = "Ninguna";
    public JugadorController objetivo; // Jugador al que se disparará

    public Button dispararButton;
    public Button recargarButton;
    public Button cubrirseButton;

    private bool accionYaSeleccionada = false;

    private void Start()
    {
        // Asignar funciones a botones
        dispararButton.onClick.AddListener(() => SeleccionarAccion("Disparar"));
        recargarButton.onClick.AddListener(() => SeleccionarAccion("Recargar"));
        cubrirseButton.onClick.AddListener(() => SeleccionarAccion("Cubrirse"));
    }

    public void SeleccionarObjetivo(JugadorController jugador)
    {
        if (jugador == this) return; // No se puede disparar a sí mismo
        objetivo = jugador;
        Debug.Log($"Jugador {id} seleccionó como objetivo al Jugador {jugador.id}");
    }

    public void SeleccionarAccion(string accion)
    {
        if (accionYaSeleccionada) return;
        accionYaSeleccionada = true;

        if (accion == "Disparar")
        {
            if (balas > 0 && objetivo != null)
            {
                accionSeleccionada = "Disparar";
                balas--; 
                Debug.Log($"Jugador {id} dispara a {objetivo.id}");
            }
            else
            {
                Debug.Log("No tienes balas o no seleccionaste un objetivo.");
                return;
            }
        }
        else if (accion == "Recargar")
        {
            accionSeleccionada = "Recargar";
            balas++;
        }
        else if (accion == "Cubrirse")
        {
            accionSeleccionada = "Cubrirse";
            cubierto = true;
        }

        Debug.Log($"Jugador {id} seleccionó: {accionSeleccionada}");
        DeshabilitarBotones();
    }

    public void ReiniciarTurno()
    {
        accionYaSeleccionada = false;
        cubierto = false;
        objetivo = null; // Reiniciar el objetivo

        // Reactivar los botones
        dispararButton.interactable = true;
        recargarButton.interactable = true;
        cubrirseButton.interactable = true;
    }

    private void DeshabilitarBotones()
    {
        dispararButton.interactable = false;
        recargarButton.interactable = false;
        cubrirseButton.interactable = false;
    }
}
