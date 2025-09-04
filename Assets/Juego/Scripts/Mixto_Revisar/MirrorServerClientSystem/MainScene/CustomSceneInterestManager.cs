using Mirror;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CustomSceneInterestManager : SceneInterestManagement
{
    public static CustomSceneInterestManager Instance { get; private set; }
    public Dictionary<NetworkConnection, string> clientMatchScene = new Dictionary<NetworkConnection, string>();

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public void RegisterPlayer(NetworkConnection conn, string sceneName)
    {
        clientMatchScene[conn] = sceneName;
    }

    // NUEVO: consultar, desregistrar y reconstruir observers
    public bool TryGetAssignedScene(NetworkConnection conn, out string sceneName)
        => clientMatchScene.TryGetValue(conn, out sceneName);

    public void Unregister(NetworkConnection conn)
    {
        if (clientMatchScene.Remove(conn))
            LogWithTime.Log($"[Interest] Unregistered {conn}.");
    }

    public void RebuildSceneObservers(string sceneName, bool initialize = false)
    {
        var scn = SceneManager.GetSceneByName(sceneName);
        if (!scn.IsValid()) return;

        foreach (var ni in NetworkServer.spawned.Values)
            if (ni != null && ni.gameObject.scene == scn)
                NetworkServer.RebuildObservers(ni, initialize);
    }
    // Espero esto solucione el bug

    public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnectionToClient newObserver)
    {
        Scene objectScene = identity.gameObject.scene;
        Scene observerScene = newObserver.identity.gameObject.scene;

        string objName = objectScene.name;
        string obsName = observerScene.name;

        // Si ambos están en la misma escena exacta -> permitir
        if (objName == obsName)
        {
            return true;
        }

        // Si objeto está en GameScene_xxxx
        if (objName.StartsWith("GameScene_"))
        {
            // Verificar si el cliente fue asignado a esta partida
            if (clientMatchScene.TryGetValue(newObserver, out string assignedScene))
            {
                if (assignedScene == objName)
                {
                    return true;
                }

                // Si el observador está en la plantilla GameScene pero corresponde a esta partida
                if (obsName == "GameScene" && assignedScene == objName)
                {
                    return true;
                }
            }
        }
        return false;
    }


}
