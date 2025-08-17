using System.Collections;
using Microsoft.Win32.SafeHandles;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public struct NameMessage : NetworkMessage
{
    public string playerName;
}

public struct LoginResultMessage : NetworkMessage
{
    public bool ok;
    public string reason; // "ok" | "duplicate"
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
        // Intencionalmente vacío: el player se crea en OnFirebaseCredentialsReceived tras validar UID si no lo dejamos así, el OnServerAddPlayer original instanciará a la fuerza un customRoomPlayer y rompe nuestro flujo
    }

#if !UNITY_SERVER
    public override void OnClientDisconnect()
    {
        // Cae la conexión (duplicado, kick, timeout, etc.)
        GoToOfflineScene();
        base.OnClientDisconnect();
    }

    public override void OnStopClient()
    {
        // StopClient() desde AuthManager (p.ej. tras LoginResult=false)
        GoToOfflineScene();
        base.OnStopClient();
    }

    private void GoToOfflineScene()
    {
        // Si tienes asignado NetworkManager.offlineScene, úsalo.
        string target = !string.IsNullOrEmpty(offlineScene) ? offlineScene : "Offline2Scene";
        if (SceneManager.GetActiveScene().name != target)
            SceneManager.LoadSceneAsync(target, LoadSceneMode.Single);
    }
#endif

    public void SendPlayerToModeScene(NetworkConnectionToClient conn, string sceneName)
    {
        ServerChangeScene(sceneName);
    }

    public void OnClientSendName(NetworkConnectionToClient conn, string playerNameFromClient)
    {
        if (conn.identity == null) return;

        var roomPlayer = conn.identity.GetComponent<CustomRoomPlayer>();
        if (roomPlayer == null) return;

        if (!AccountManager.Instance.TryGetFirebaseCredentials(conn, out var creds))
        {
            Debug.LogWarning("[CustomNetworkManager] No hay credenciales para esta conexión.");
            return;
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

        // Limpiar índices de sesión para no dejar “fantasmas”
        AccountManager.Instance.RemoveConnection(conn);

        // Llamar a base para destruir objetos asociados
        base.OnServerDisconnect(conn);

        Debug.Log($"[SERVER] Conexión desconectada: {conn.connectionId}");
    }

    private void OnReceiveNameMessage(NetworkConnectionToClient conn, NameMessage msg)
    {
        OnClientSendName(conn, msg.playerName);
    }

    #region Disconnect_Duplicate_User

    // Antes de crear el roomPlayer en OnFirebaseCredentialsReceived, corta si el UID ya está logueado.
    private void OnFirebaseCredentialsReceived(NetworkConnectionToClient conn, FirebaseCredentialMessage msg)
    {
        Debug.Log($"[SERVER] FirebaseCredentialMessage recibido: UID = {msg.uid}");

        if (string.IsNullOrWhiteSpace(msg.uid))
        {
            conn.Send(new LoginResultMessage { ok = false, reason = "empty_uid" });
            StartCoroutine(DisconnectNextFrame(conn));
            return;
        }

        if (AccountManager.Instance.IsUidInUse(msg.uid, out var existing) && existing != conn)
        {
            // <- Duplicado: avisamos y desconectamos en el próximo frame
            conn.Send(new LoginResultMessage { ok = false, reason = "duplicate" });
            StartCoroutine(DisconnectNextFrame(conn));
            return;
        }

        // OK: registramos credenciales y mandamos ACK de éxito
        AccountManager.Instance.RegisterFirebaseCredentials(conn, msg.uid);
        conn.Send(new LoginResultMessage { ok = true, reason = "ok" });

        // Instanciamos jugador
        var playerObj = Instantiate(roomPlayerPrefab);
        var crp = playerObj.GetComponent<CustomRoomPlayer>();

        crp.firebaseUID = msg.uid;
        crp.playerName = "Desconocido";

        NetworkServer.AddPlayerForConnection(conn, playerObj);
    }

    private System.Collections.IEnumerator DisconnectNextFrame(NetworkConnectionToClient c)
    {
        yield return null; // garantiza que el LoginResult llegue
        c.Disconnect();
    }

    #endregion

}
