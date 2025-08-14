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
        
        // Registrar el handler que registra al usuario en el servidor Mirror y le crea un CustomRoomPlayer
        NetworkServer.RegisterHandler<FirebaseCredentialMessage>(OnFirebaseCredentialsReceived);

        // Registrar el handler que registra al nombre del usuario y lo actualiza cada ves que se llama desde NicknameUI
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
        yield return new WaitForSecondsRealtime(0.1f);

        SceneManager.LoadSceneAsync("LobbySceneCasual", LoadSceneMode.Additive);
        SceneManager.LoadSceneAsync("LobbySceneRanked", LoadSceneMode.Additive);
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        GameObject playerObj = Instantiate(roomPlayerPrefab);
        NetworkServer.AddPlayerForConnection(conn, playerObj);
        Debug.Log("[SERVER] CustomRoomPlayer asignado en OnServerAddPlayer");
    }

    public void SendPlayerToModeScene(NetworkConnectionToClient conn, string sceneName)
    {
        ServerChangeScene(sceneName);
    }

    public void OnClientSendName(NetworkConnectionToClient conn, string playerNameFromClient)
    {
        if (conn.identity == null) return;

        var roomPlayer = conn.identity.GetComponent<CustomRoomPlayer>();
        if (roomPlayer == null) return;

        if (AccountManager.Instance.TryGetFirebaseCredentials(conn, out var creds))
        {
            string uid = creds.uid;
        }

        if (string.IsNullOrEmpty(creds.uid))
        {
            Debug.LogWarning("[CustomNetworkManager] UID vacío, no se puede asignar nombre.");
            return;
        }

        // Intentar obtener nombre desde Firestore
        StartCoroutine(FirebaseServerClient.GetNicknameFromFirestore(creds.uid, (nicknameInFirestore) =>
        {
            string finalName = !string.IsNullOrEmpty(nicknameInFirestore) ? nicknameInFirestore : playerNameFromClient;

            roomPlayer.playerName = finalName;
            AccountManager.Instance.UpdatePlayerName(conn, finalName);

            Debug.Log($"[SERVER] Nombre asignado al jugador con UID {creds.uid}: {finalName}");

            // Si no había nombre en Firestore, lo guardamos
            if (string.IsNullOrEmpty(nicknameInFirestore))
            {
                StartCoroutine(FirebaseServerClient.UpdateNickname(creds.uid, finalName));
            }
        }));
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

    private void OnFirebaseCredentialsReceived(NetworkConnectionToClient conn, FirebaseCredentialMessage msg)
    {
        Debug.Log($"[SERVER] FirebaseCredentialMessage recibido: UID = {msg.uid}");

        if (conn.identity != null)
        {
            Debug.LogWarning($"[SERVER] La conexión {conn.connectionId} ya tiene un jugador asignado.");
            return;
        }

        GameObject playerObj = Instantiate(roomPlayerPrefab);
        CustomRoomPlayer crp = playerObj.GetComponent<CustomRoomPlayer>();
        AccountManager.Instance.RegisterFirebaseCredentials(conn, msg.uid);

        crp.firebaseUID = msg.uid;
        crp.playerName = "Desconocido"; // Lo puedes actualizar luego con otro mensaje si quieres

        NetworkServer.AddPlayerForConnection(conn, playerObj);
        Debug.Log($"[SERVER] Jugador instanciado con UID {msg.uid}");
    }

}
