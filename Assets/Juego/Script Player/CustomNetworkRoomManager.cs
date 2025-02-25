using Mirror;
using UnityEngine;

public class CustomNetworkRoomManager : NetworkRoomManager
{ 
    /* public override void Awake()
    {
        if (!NetworkServer.active)
        {
            StartServer();
            Debug.Log("Servidor iniciado correctamente ;)");
        }
    } */

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log("Entro un cliente");
    }



    /*public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        base.OnServerAddPlayer(conn);
        PlayerController mySyncVar = conn.identity.GetComponent<PlayerController>();
        Debug.Log("Un jugador se ha agregado" + numPlayers);


        float R = Random.Range(0, 1f);
        float G = Random.Range(0, 1f);
        float B = Random.Range(0, 1f);

        mySyncVar.SetHealthPlayer(10);
        mySyncVar.SetColorPlayer(new Color(R, G, B, 1f));
    }*/
}
