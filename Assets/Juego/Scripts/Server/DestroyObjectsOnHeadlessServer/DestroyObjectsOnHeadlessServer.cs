using UnityEngine;
using Mirror;

public class DestroyObjectsOnHeadlessServer : MonoBehaviour
{
    private void Awake()
    {
        if (NetworkServer.active && !NetworkClient.active)
        {
            Destroy(gameObject);
        }
    }
}
