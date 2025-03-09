using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Mirror;

public class DebugConsole : NetworkBehaviour
{
    public GameObject consolePanel;
    public TMP_Text logText;
    public ScrollRect scrollRect;

    private Queue<string> logHistory = new Queue<string>();
    private bool isConsoleVisible = false;
    private int maxMessages = 50;

    private void Awake()
    {
        consolePanel.SetActive(false);
        Application.logMessageReceived += HandleLog; //Capturar mensajes de consola de Unity
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.M))
        {
            isConsoleVisible = !isConsoleVisible;
            consolePanel.SetActive(isConsoleVisible);

            //Llamamos al Command en el GameManager
            if (isLocalPlayer)
            {
                CmdTogglePause(isConsoleVisible);
            }
        }
    }

    //Command que enviará la señal de pausa al GameManager
    [Command]
    private void CmdTogglePause(bool consoleOpened)
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetPause(consoleOpened); //Llamamos al gameManager con el nuevo estado de true or false para pausa
        }
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (logHistory.Count >= maxMessages)
        {
            logHistory.Dequeue(); //Eliminar mensajes antiguos
        }

        logHistory.Enqueue(logString); // Agregar mensaje al historial

        logText.text = string.Join("\n", logHistory); //Mostrar todos los logs en UI
        Canvas.ForceUpdateCanvases(); //Actualizar Canvas
        scrollRect.verticalNormalizedPosition = 0f; //Auto-Scroll al último mensaje
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog; //Evita referencias al destruir
    }
}
