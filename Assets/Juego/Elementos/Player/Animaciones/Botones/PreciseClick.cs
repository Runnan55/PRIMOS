using UnityEngine;
using UnityEngine.UI;

public class PreciseClick : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<Image>().alphaHitTestMinimumThreshold = 0.1f; // Para no clikar en zonas transparentes del botón
    }
}
