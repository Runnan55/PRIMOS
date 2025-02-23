using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class TurnoManager : MonoBehaviour
{
    public Slider tiempoSlider;
    public Text tiempoTexto; // Opcional, si quieres mostrar el número en pantalla
    public float tiempoMaximo = 10f; // Tiempo por turno
    private float tiempoRestante;
    //private bool turnoEnCurso = false;

    public System.Action OnTiempoFinalizado; // Evento para cuando acabe el tiempo

    private void Start()
    {
    }

    public void IniciarTurno()
    {
        tiempoRestante = tiempoMaximo;
        tiempoSlider.maxValue = tiempoMaximo;
        tiempoSlider.value = tiempoMaximo;
        StartCoroutine(ContadorTurno());
    }

    private IEnumerator ContadorTurno()
    {
        while (tiempoRestante > 0)
        {
            tiempoRestante -= Time.deltaTime;
            tiempoSlider.value = tiempoRestante;

            if (tiempoTexto != null)
                tiempoTexto.text = Mathf.Ceil(tiempoRestante).ToString(); // Actualizar segundos restantes
                Debug.Log("Tiempo restante: " + Mathf.Ceil(tiempoRestante));

            yield return null;
        }

        // Tiempo agotado
        if (OnTiempoFinalizado != null) OnTiempoFinalizado.Invoke();
    }
}
