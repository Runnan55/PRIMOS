using System.Collections;
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
        string playerId = System.Guid.NewGuid().ToString();
        AccountManager.Instance.RegisterPlayer(conn, playerName, playerId);

        GameObject playerObj = Instantiate(roomPlayerPrefab);
        var roomPlayer = playerObj.GetComponent<CustomRoomPlayer>();
        roomPlayer.playerId = playerId;
        roomPlayer.playerName = playerName; //<--- YA LE PASAMOS EL NOMBRE

        NetworkServer.AddPlayerForConnection(conn, playerObj);
    }

    private void OnReceiveNameMessage(NetworkConnectionToClient conn, NameMessage msg)
    {
        OnClientSendName(conn, msg.playerName);
    }

}
