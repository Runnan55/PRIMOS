using Mirror;
using UnityEngine;

public class CustomNetworkManager : NetworkManager
{
    public GameObject roomPlayerPrefab;

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        if (conn.identity != null)
        {
            Debug.Log("[SERVER] El jugador ya tiene un RoomPlayer asignado.");
            return;
        }

        GameObject player = Instantiate(roomPlayerPrefab);
        NetworkServer.AddPlayerForConnection(conn, player);
    }

    public void SendPlayerToModeScene(NetworkConnectionToClient conn, string sceneName)
    {
        ServerChangeScene(sceneName);
    }
}
