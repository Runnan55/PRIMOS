using UnityEngine;
using UnityEngine.UI;
using Mirror;
using TMPro;
using Mirror.SimpleWeb;

public class TestQuickLogin : MonoBehaviour
{
    public AuthManager authManager;
    public NetworkManager networkManager;
    public TMP_InputField emailField;
    public TMP_InputField passwordField;
    public Button autoFillButton;

    void Start()
    {
        autoFillButton.onClick.AddListener(AutoConfigure);
    }

    private void AutoConfigure()
    {
        // 1. Autocompletar email y password
        emailField.text = "test@gmail.com";
        passwordField.text = "qwerty1";

        // 2. Modificar transporte (SimpleWebTransport)
        if (networkManager != null && networkManager.transport is SimpleWebTransport swt)
        {
            swt.clientWebsocketSettings.ClientPortOption = WebsocketPortOption.SpecifyPort;
            swt.clientWebsocketSettings.CustomClientPort = 2777;
            swt.clientUseWss = false;
            Debug.Log("SimpleWebTransport configurado para test: puerto 2777, WSS desactivado.");
        }

        // 3. Cambiar dirección del servidor
        networkManager.networkAddress = "localhost";
        Debug.Log("Dirección de red cambiada a localhost.");
    }
}
