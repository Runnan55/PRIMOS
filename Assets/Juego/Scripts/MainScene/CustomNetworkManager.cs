using System.Collections;
using Microsoft.Win32.SafeHandles;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public struct NameMessage : NetworkMessage
{
    public string playerName;
}

public class CustomNetworkManager : NetworkManager
{
    public GameObject roomPlayerPrefab;
    public GameObject gameManagerPrefab;
    public GameObject playerControllerPrefab;

    public override void OnStartServer()
    {
        base.OnStartServer();

        StartCoroutine (LoadLobbyScenesWithDelay());

        // REGISTRAR EL HANDLER DEL MENSAJE
        NetworkServer.RegisterHandler<NameMessage>(OnReceiveNameMessage);
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

    private void OnReceiveNameMessage(NetworkConnectionToClient conn, NameMessage msg)
    {
        OnClientSendName(conn, msg.playerName);
    }
}
