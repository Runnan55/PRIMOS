using UnityEngine;
using Mirror;
using TMPro;
using UnityEngine.UI;

public class CustomNetworkRoomPlayer : NetworkRoomPlayer
{
    [Header("UI References")]
    public GameObject playerPanel;
    public TextMeshProUGUI playerNameText;
    public Button readyButton;
    public TextMeshProUGUI readyButtonText;

    [SyncVar] private Vector3 assignedPosition;

    public override void OnStartClient()
    {
        base.OnStartClient();
        SetupUI();
        UpdateUI();

        if (isServer)
        {
            AssignPositionOnServer();
        }
    }

    void SetupUI()
    {
        if (isLocalPlayer)
        {
            readyButton.onClick.AddListener(OnReadyButtonClicked);
        }
    }

    void OnReadyButtonClicked()
    {
        if (readyToBegin)
        {
            CmdChangeReadyState(false);
        }
        else
        {
            CmdChangeReadyState(true);
        }
    }

    public override void ReadyStateChanged(bool oldReadyState, bool newReadyState)
    {
        UpdateUI();
    }

    void UpdateUI()
    {
        if (playerNameText != null)
        {
            playerNameText.text = $"Player {index + 1}";
        }

        if (readyToBegin)
        {
            readyButtonText.text = "Cancel";
        }
        else
        {
            readyButtonText.text = "Ready";
        }
    }

    public override void OnClientEnterRoom()
    {
        base.OnClientEnterRoom();
        playerPanel.SetActive(true);
        transform.position = assignedPosition;  // Asegura que el cliente tome la posición asignada
    }

    public override void OnClientExitRoom()
    {
        base.OnClientExitRoom();
        playerPanel.SetActive(false);
    }

    [Server]
    /*void AssignPositionOnServer()
    {
        if (playerPositions.Length > index)
        {
            assignedPosition = playerPositions[index].position;
            RpcSetPosition(assignedPosition);
        }
        else
        {
            Debug.LogWarning("No hay posiciones suficientes definidas.");
        }
    }*/
    void AssignPositionOnServer()
    {
        float yOffset = -100f * index; // Calcula el offset basado en el índice del jugador
        assignedPosition = new Vector3(0f, yOffset, 0f);

        RpcSetPlayerPanelPosition(assignedPosition);
    }

    [ClientRpc]
    void RpcSetPlayerPanelPosition(Vector3 position)
    {
        if (playerPanel != null)
        {
            RectTransform rectTransform = playerPanel.GetComponent<RectTransform>();
            rectTransform.anchoredPosition = position;
        }
    }


    [ClientRpc]
    void RpcSetPosition(Vector3 position)
    {
        transform.position = position;
    }
}