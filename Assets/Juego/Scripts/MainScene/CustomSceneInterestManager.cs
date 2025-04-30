using Mirror;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CustomSceneInterestManager : SceneInterestManagement
{
    public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnectionToClient newObserver)
    {
        Scene objectScene = identity.gameObject.scene;
        Scene observerScene = newObserver.identity.gameObject.scene;

        Debug.Log($"[CustomSceneInterestManager] Revisando visibilidad: {identity.name} en {objectScene.name} => {observerScene.name}");

        string objName = objectScene.name;
        string obsName = observerScene.name;

        if (objName.StartsWith("GameScene_") && obsName == "GameScene")
        {
            Debug.Log("[CustomSceneInterestManager] ¡Permitiendo visibilidad forzada!");
            return true;
        }

        return objectScene == observerScene;
    }
}
