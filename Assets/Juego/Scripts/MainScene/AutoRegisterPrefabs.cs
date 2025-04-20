using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class AutoRegisterPrefabs : MonoBehaviour
{
    [Header("Folder path inside Resources")]
    public string resourcesFolder = "SpawnablePrefabs";

    private void Awake()
    {
        List<GameObject> prefabs = new List<GameObject>();

        //Cargar prefabs desde Resources
        GameObject[] loadedPrefabs = Resources.LoadAll<GameObject>(resourcesFolder);

        foreach (GameObject prefab in loadedPrefabs)
        {
            if (prefab.GetComponent<NetworkIdentity>() != null)
            {
                prefabs.Add(prefab);
            }
        }

        if (NetworkManager.singleton != null)
        {
            NetworkManager.singleton.spawnPrefabs = prefabs;
            Debug.Log($"Registrados {prefabs.Count} prefabs automáticamente en NetworkManager");
        }
    }
}
