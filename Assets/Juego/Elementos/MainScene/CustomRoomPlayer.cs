using Mirror;
using UnityEngine;

public class CustomRoomPlayer : NetworkBehaviour
{
    [SyncVar] public string playerName;
    [SyncVar] public bool isReady = false;
    [SyncVar] public bool isAdming = false;
    [SyncVar] public string currentMatchId;

    public static CustomRoomPlayer LocalInstance;

    private void Awake()
    {
        // Solo si es el cliente local
        if (isLocalPlayer)
        {
            if (LocalInstance != null)
            {
                Destroy(gameObject); // Ya existe uno, destruir este
                return;
            }

            LocalInstance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (LocalInstance == this)
        {
            LocalInstance = null;
        }
    }

    [Command]
    public void CmdSetPlayerName(string name)
    {
        playerName = name;
    }

    [Command]
    public void CmdRequestSceneChange(string sceneName)
    {
        Debug.Log($"[SERVER] {playerName} pidió ir a escena: {sceneName}");
        NetworkManager.singleton.ServerChangeScene(sceneName);
    }

    [Command]
    public void CmdSetReady()
    {
        isReady = !isReady;
        Debug.Log($"[SERVER] {playerName} está {(isReady ? "LISTO" : "NO LISTO")}");
    }

    [TargetRpc]
    public void TargetSendMatchList(NetworkConnection target, string[] matchList)
    {
        // llenar UI del cliente con nombres de partidas
    }

    [TargetRpc]
    public void TargetSendError(NetworkConnection target, string message)
    {
        Debug.LogError($"[SERVER] Error enviado a {playerName}: {message}");
    }
}
