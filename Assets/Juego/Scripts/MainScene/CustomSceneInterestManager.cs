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

    public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnectionToClient newObserver)
    {
        Scene objectScene = identity.gameObject.scene;
        Scene observerScene = newObserver.identity.gameObject.scene;

        Debug.Log($"[CustomSceneInterestManager] Revisando visibilidad: {identity.name} en {objectScene.name} => {observerScene.name}");

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
                    Debug.Log($"[CustomSceneInterestManager] Cliente {newObserver} ve objeto en su partida {objName}");
                    return true;
                }

                // Si el observador está en la plantilla GameScene pero corresponde a esta partida
                if (obsName == "GameScene" && assignedScene == objName)
                {
                    Debug.Log($"[CustomSceneInterestManager] Cliente en plantilla GameScene ve objeto de su partida {objName}");
                    return true;
                }
            }
        }

        /*
        // Si objeto está en GameScene_XXXX y observador en GameScene (clientes) -> permitir visibilidad forzada
        if (objName.StartsWith("GameScene_") && obsName == "GameScene")
        {
            Debug.Log($"[CustomSceneInterestManager] Permitiendo visibilidad especial para {identity.name} ({objName} vs {obsName})");
            return true;
        }*/


        // Por defecto, no permitir

        return false;
    }


}
