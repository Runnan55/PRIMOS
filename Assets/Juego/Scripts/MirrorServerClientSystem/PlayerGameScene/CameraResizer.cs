using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraResizer1920x1080 : MonoBehaviour
{
    [Header("Configuración de aspecto base")]
    public float targetOrthoSize = 18f; // Usa tu Size actual de 1920x1080
    public float targetWidth = 1920f;
    public float targetHeight = 1080f;

    private Camera cam;
    private float lastScreenWidth = 0f;
    private float lastScreenHeight = 0f;

    void Awake()
    {
        cam = GetComponent<Camera>();
        AdjustCamera();
    }

    void Start()
    {
        AdjustCamera();
    }

    void Update()
    {
        // Solo ajusta si la resolución realmente cambió (optimización)
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
        {
            AdjustCamera();
        }
    }

    void AdjustCamera()
    {
        float targetAspect = targetWidth / targetHeight;
        float windowAspect = (float)Screen.width / Screen.height;

        // Para que siempre "llene" la pantalla como el Canvas (podrías cambiar esta lógica si quieres otro comportamiento)
        if (windowAspect >= targetAspect)
        {
            // Pantalla más ancha o igual que 16:9: se ve el alto esperado, puede recortar a los lados
            cam.orthographicSize = targetOrthoSize;
        }
        else
        {
            // Pantalla más alta: ajusta el size para que no se corte arriba/abajo, puede dejar bandas a los lados
            cam.orthographicSize = targetOrthoSize * (targetAspect / windowAspect);
        }

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
    }
}
