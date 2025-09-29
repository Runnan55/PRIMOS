using System;
using System.Collections;
using System.Linq;
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

    private static CustomNetworkManager _instance;

    public override void Awake()
    {
        if (_instance != null && _instance != this)
        {
            string scene = gameObject.scene.name;
            string path = GetPath(transform);

            Debug.LogError(
                $"[CustomNetworkManager] Instancia duplicada detectada y destruida.\n" +
                $" - Instancia existente: {_instance.gameObject.name} (scene: {_instance.gameObject.scene.name})\n" +
                $" - Instancia destruida: {gameObject.name} (scene: {scene}, path: {path})",
                gameObject
            );

            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        LogWithTime.Log($"[CustomNetworkManager] Instancia creada en scene {gameObject.scene.name}, path: {GetPath(transform)}");

        base.Awake();
    }

    private static string GetPath(Transform current)
    {
        string path = current.name;
        while (current.parent != null)
        {
            current = current.parent;
            path = current.name + "/" + path;
        }
        return path;
    }

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
        LogWithTime.Log("[SERVER] Recibido EmptyTimerMessage desde cliente.");

        if (EventTimeManager.Instance == null)
        {
            LogWithTime.LogError("[SERVER] EventTimeManager.Instance es NULL. No puedo responder.");
        }
        else
        {
            LogWithTime.Log("[SERVER] Llamando a EventTimeManager.HandleTimeRequest...");
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
            LogWithTime.LogWarning("[CustomNetworkManager] No hay credenciales para esta conexión.");
            return;
        }

        if (string.IsNullOrEmpty(creds.uid))
        {
            LogWithTime.LogWarning("[CustomNetworkManager] UID vacío, no se puede asignar nombre.");
            return;
        }


        // Intentar obtener nombre desde Firestore
        StartCoroutine(FirebaseServerClient.GetNicknameFromFirestore(creds.uid, (nicknameInFirestore) =>
        {
            string finalName = !string.IsNullOrEmpty(nicknameInFirestore) ? nicknameInFirestore : playerNameFromClient;

            roomPlayer.playerName = finalName;
            AccountManager.Instance.UpdatePlayerName(conn, finalName);

            LogWithTime.Log($"[SERVER] Nombre asignado al jugador con UID {creds.uid}: {finalName}");

            // Si no había nombre en Firestore, lo guardamos
            if (string.IsNullOrEmpty(nicknameInFirestore))
            {
                StartCoroutine(FirebaseServerClient.UpdateNickname(creds.uid, finalName));
            }
        }));
    }

    /*public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // 1) Quitar autoridad del PlayerController ANTES del base
        if (conn != null && conn.identity != null)
        {
            var crp = conn.identity.GetComponent<CustomRoomPlayer>();
            if (crp != null && crp.linkedPlayerController != null)
            {
                var pcNI = crp.linkedPlayerController.GetComponent<NetworkIdentity>();
                if (pcNI != null && pcNI.connectionToClient != null)
                    pcNI.RemoveClientAuthority();     // evita que Mirror destruya el PC

                crp.linkedPlayerController.GManager?.PlayerWentAfk(crp.linkedPlayerController);
            }
        }

        // limpia tus indices primero (no toca el diccionario de Mirror)
        AccountManager.Instance.RemoveConnection(conn);

        int before = NetworkServer.connections.Count;

        // base.OnServerDisconnect hace la parte importante:
        // - destruye player objects
        // - remueve la conn de NetworkServer.connections
        base.OnServerDisconnect(conn);

        int after = NetworkServer.connections.Count;

        LogWithTime.Log($"[SERVER] disconnect id={conn.connectionId}. now_active={after} (was {before})");
    }*/

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // 0) Defensive null checks
        var crp = conn != null && conn.identity != null
            ? conn.identity.GetComponent<CustomRoomPlayer>()
            : null;

        // 1) Remove scene-interest mapping ASAP
        //    (avoid lingering observers/filters for this conn)
        CustomSceneInterestManager.Instance?.Unregister(conn);

        // 2) Try to resolve the PlayerController to drop authority
        PlayerController pc = null;

        if (crp != null && crp.linkedPlayerController != null)
        {
            pc = crp.linkedPlayerController;
        }
        else if (crp != null)
        {
            // Fallback: search by logical identity (playerId / firebaseUID)
            string pid = crp.playerId;
            string fuid = crp.firebaseUID;

            foreach (var kv in NetworkServer.spawned)
            {
                var ni = kv.Value;
                if (ni == null) continue;
                var candidate = ni.GetComponent<PlayerController>();
                if (candidate == null) continue;

                bool match =
                    (!string.IsNullOrEmpty(pid) && candidate.playerId == pid) ||
                    (!string.IsNullOrEmpty(fuid) && candidate.firebaseUID == fuid);

                if (match) { pc = candidate; break; }
            }
        }

        if (pc != null)
        {
            var ni = pc.netIdentity != null ? pc.netIdentity : pc.GetComponent<NetworkIdentity>();
            if (ni != null && ni.connectionToClient != null)
            {
                ni.RemoveClientAuthority(); // critical: drop authority before base
            }

            // Optional but useful: if its GameManager is still alive, mark AFK
            try
            {
                var gm = pc.GManager; // uses your cached getter over gameManagerNetId
                if (gm != null)
                {
                    gm.PlayerWentAfk(pc);
                }
            }
            catch { /* swallow: scene may be unloading */ }
        }
        else
        {
            LogWithTime.LogWarning("[DISCO] Could not resolve PlayerController on disconnect.");
        }

        // 3) Cleanup account mappings (tokens, uid->conn)
        AccountManager.Instance?.RemoveConnection(conn);

        int before = NetworkServer.connections.Count;
        // 4) Now let Mirror proceed
        base.OnServerDisconnect(conn);

        int after = NetworkServer.connections.Count;

        LogWithTime.Log($"[SERVER] disconnect id={conn.connectionId}. now_active={after} (was {before})");
    }

    private PlayerController TryResolvePlayerController(NetworkConnectionToClient conn)
    {
        var crp = (conn != null && conn.identity != null)
            ? conn.identity.GetComponent<CustomRoomPlayer>()
            : null;

        if (crp == null) return null;

        // 1) si ya hay referencia, usarla
        if (crp.linkedPlayerController != null)
            return crp.linkedPlayerController;

        // 2) fallback: buscar por escena de la partida y playerId
        if (!string.IsNullOrEmpty(crp.currentMatchId))
        {
            var match = MatchHandler.Instance?.GetMatch(crp.currentMatchId);
            if (match != null)
            {
                var scene = SceneManager.GetSceneByName(match.sceneName);
                if (scene.IsValid())
                {
                    var pcs = scene.GetRootGameObjects()
                                   .SelectMany(g => g.GetComponentsInChildren<PlayerController>(true));
                    var pc = pcs.FirstOrDefault(p => p != null && p.playerId == crp.playerId);
                    if (pc != null)
                    {
                        crp.linkedPlayerController = pc; // mantener la referencia
                        return pc;
                    }
                }
            }
        }

        return null;
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        int after = NetworkServer.connections.Count;
        LogWithTime.Log($"[SERVER] connect id={conn.connectionId}. now_active={after}");
    }


    private void OnReceiveNameMessage(NetworkConnectionToClient conn, NameMessage msg)
    {
        OnClientSendName(conn, msg.playerName);
    }

    #region Disconnect_Duplicate_User

    // Antes de crear el roomPlayer en OnFirebaseCredentialsReceived, corta si el UID ya está logueado.
    private void OnFirebaseCredentialsReceived(NetworkConnectionToClient conn, FirebaseCredentialMessage msg)
    {
        LogWithTime.Log($"[SERVER] FirebaseCredentialMessage recibido: UID = {msg.uid}");

        if (string.IsNullOrWhiteSpace(msg.uid))
        {
            conn.Send(new LoginResultMessage { ok = false, reason = "empty_uid" });
            StartCoroutine(DisconnectNextFrame(conn));
            return;
        }

        // Si ya existe el UID en otra conexion, reasigna el player al nuevo socket
        if (AccountManager.Instance.IsUidInUse(msg.uid, out var oldConn) && oldConn != conn)
        {
            var oldIdentity = oldConn.identity;
            if (oldIdentity != null)
            {
                // API nueva con enum
                NetworkServer.ReplacePlayerForConnection(
                    conn,
                    oldIdentity.gameObject,
                    ReplacePlayerOptions.KeepAuthority // mantiene activo y con autoridad
                );

                oldConn.Disconnect();
                AccountManager.Instance.RemoveConnection(oldConn);
                AccountManager.Instance.RegisterFirebaseCredentials(conn, msg.uid);

                LogWithTime.Log("[Auth] Reclaim OK -> uid=" + msg.uid +
                          " newConn=" + conn.connectionId +
                          " tookOverOldConn=" + oldConn.connectionId);
                return; // no instancies un CRP nuevo
            }

            // Fallback: no hay identity anterior, rechazo el nuevo
            conn.Send(new LoginResultMessage { ok = false, reason = "duplicate" });
            StartCoroutine(DisconnectNextFrame(conn));
            LogWithTime.Log("[Auth] Duplicate login rejected -> uid=" + msg.uid +
                      " connId=" + conn.connectionId +
                      " reason=no-old-identity-to-reclaim");
            return;
        }


        // OK: registramos credenciales y mandamos ACK de éxito
        AccountManager.Instance.RegisterFirebaseCredentials(conn, msg.uid);
        conn.Send(new LoginResultMessage { ok = true, reason = "ok" });

        // Instanciamos jugador
        var playerObj = Instantiate(roomPlayerPrefab);
        NetworkServer.AddPlayerForConnection(conn, playerObj);

        var crp = playerObj.GetComponent<CustomRoomPlayer>();

        crp.firebaseUID = msg.uid;
        crp.playerName = "Desconocido";

        crp.StartCoroutine(DelayedTryRejoin(crp, msg.uid));
    }

    private IEnumerator DelayedTryRejoin(CustomRoomPlayer crp, string uid)
    {
        yield return null; // esperar un frame
        if (!string.IsNullOrEmpty(uid))
        {
            bool ok = MatchHandler.Instance.TryRejoinActiveMatchByUid(crp, uid);
            if (!ok)
                LogWithTime.Log("[REJOINDBG] No se encontró partida activa para uid=" + uid);
        }
        else
        {
            LogWithTime.LogWarning("[REJOINDBG] UID vacío al intentar rejoin.");
        }
    }

    private System.Collections.IEnumerator DisconnectNextFrame(NetworkConnectionToClient c)
    {
        yield return null;
        LogWithTime.Log("[Auth] DisconnectNextFrame -> connId=" + c.connectionId + " cause=duplicate");
        c.Disconnect();
    }

    #endregion

}

public static class LogWithTime
{
    public static void Log(string msg)
    {
        Debug.Log(Format("", msg));
    }

    public static void LogWarning(string msg)
    {
        Debug.LogWarning(Format("WARN", msg));
    }

    public static void LogError(string msg)
    {
        Debug.LogError(Format("ERROR", msg));
    }

    private static string Format(string level, string msg)
    {
        DateTime localNow = DateTime.Now; // Hora local del sistema
        return $"[{level}] {localNow:yyyy-MM-dd HH:mm:ss} {msg}";
    }
}

