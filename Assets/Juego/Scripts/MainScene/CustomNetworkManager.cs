using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CustomNetworkManager : NetworkManager
{
    public GameObject roomPlayerPrefab;
    public override void OnStartServer()
    {
        base.OnStartServer();

        StartCoroutine (LoadLobbyScenesWithDelay());
    }
    private IEnumerator LoadLobbyScenesWithDelay()
    {
        yield return new WaitForSeconds(0.1f);

        SceneManager.LoadSceneAsync("LobbySceneCasual", LoadSceneMode.Additive);
        SceneManager.LoadSceneAsync("LobbySceneRanked", LoadSceneMode.Additive);
    }

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
