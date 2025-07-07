using System.Collections;
using Microsoft.Win32.SafeHandles;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public struct NameMessage : NetworkMessage
{
    public string playerName;
}

public struct EmptyTimerMessage : NetworkMessage { }

public class CustomNetworkManager : NetworkManager
{
    public GameObject roomPlayerPrefab;
    public GameObject gameManagerPrefab;

    public override void OnStartServer()
    {
        base.OnStartServer();

        StartCoroutine (LoadLobbyScenesWithDelay());

        // REGISTRAR EL HANDLER DEL MENSAJE
        NetworkServer.RegisterHandler<NameMessage>(OnReceiveNameMessage);

        //Registrar el mensaje de tiempo
        NetworkServer.RegisterHandler<EmptyTimerMessage>(OnClientRequestedTime);
    }

    private void OnClientRequestedTime(NetworkConnectionToClient conn, EmptyTimerMessage msg)
    {
        Debug.Log("[SERVER] Recibido EmptyTimerMessage desde cliente.");

        if (EventTimeManager.Instance == null)
        {
            Debug.LogError("[SERVER] EventTimeManager.Instance es NULL. No puedo responder.");
        }
        else
        {
            Debug.Log("[SERVER] Llamando a EventTimeManager.HandleTimeRequest...");
            EventTimeManager.Instance.HandleTimeRequest(conn);
        }
    }

    private IEnumerator LoadLobbyScenesWithDelay()
    {
        yield return new WaitForSeconds(0.1f);

        SceneManager.LoadSceneAsync("LobbySceneCasual", LoadSceneMode.Additive);
        SceneManager.LoadSceneAsync("LobbySceneRanked", LoadSceneMode.Additive);
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);

        if (conn.identity == null)
        {
            GameObject playerObj = Instantiate(roomPlayerPrefab);
            NetworkServer.AddPlayerForConnection(conn, playerObj);
            Debug.Log("[SERVER] CustomRoomPlayer asignado automáticamente en OnServerConnect");
        }
    }

    public void SendPlayerToModeScene(NetworkConnectionToClient conn, string sceneName)
    {
        ServerChangeScene(sceneName);
    }

    public void OnClientSendName(NetworkConnectionToClient conn, string playerName)
    {
        //Sí el jugador ya tiene un CustomRoomPlayer asignado
        if (conn.identity != null)
        {
            var existingRoomPlayer = conn.identity.GetComponent<CustomRoomPlayer>();

            if (existingRoomPlayer !=null)
            {
                existingRoomPlayer.playerName = playerName;

                if (!AccountManager.Instance.HasDataFor(conn))
                {
                    string playerId = System.Guid.NewGuid().ToString();
                    AccountManager.Instance.RegisterPlayer(conn, playerName, playerId);
                    existingRoomPlayer.playerId = playerId;
                }
                else
                {
                    // Solo Actualizar el nombre en AccountManager si ya está registrado
                    AccountManager.Instance.UpdatePlayerName(conn, playerName);
                }

                Debug.Log($"[SERVER] Se actualizó el nombre del jugador existente a: {playerName}");
                return;
            }
        }
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // Primero limpia cualquier dato o estado de jugador antes de desconectar
        if (conn.identity != null)
        {
            var player = conn.identity.GetComponent<CustomRoomPlayer>();
            if (player != null)
            {
                // Llama a cualquier lógica de limpieza manual, por ejemplo:
                player.OnStopServer(); // o tu propio método de limpieza
            }
        }

        // Llamar a base para destruir objetos asociados
        base.OnServerDisconnect(conn);

        Debug.Log($"[SERVER] Conexión desconectada: {conn.connectionId}");
    }

    private void OnReceiveNameMessage(NetworkConnectionToClient conn, NameMessage msg)
    {
        OnClientSendName(conn, msg.playerName);
    }
}
