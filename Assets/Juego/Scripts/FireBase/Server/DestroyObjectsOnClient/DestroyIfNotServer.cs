using UnityEngine;
using Mirror;

public class DestroyIfNotServer : MonoBehaviour
{
    void Awake()
    {
        if (!NetworkServer.active)
        {
            Destroy(gameObject);
        }
    }
}