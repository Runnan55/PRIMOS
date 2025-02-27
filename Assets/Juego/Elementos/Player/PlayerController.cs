using Mirror;
using UnityEngine;
using UnityEngine.UI;

public enum ActionType
{
    None, //Opción por si el jugador no elige nada
    Shoot,
    Reload,
    Cover
}

public class PlayerAction
{
    public ActionType type;
    public PlayerController target; //Se usa solo para el disparo

    public PlayerAction(ActionType type,PlayerController target = null)
    {
        this.type = type;
        this.target = target;
    }
}

public class PlayerController : NetworkBehaviour
{
    [SyncVar] public bool isAlive = true;
    [SyncVar] public int ammo = 3;
    [SyncVar] public int health = 3;
    private bool isCovering = false;

    public void StartTurn()
    {
        if (!isAlive) return;
        isCovering = false; //Quitar cobertura al acabar cada turno
    }

    [Server]
    public void AttemptShoot(PlayerController target)
    {
        if (ammo <= 0 || target == null || !target.isAlive || target.isCovering) return;

        ammo--;
        target.TakeDamage();
    }

    [Server]
    public void Reload()
    {
        ammo++;
    }

    [Server]
    public void Cover()
    {
        isCovering = true;
    }

    [Server]
    public void TakeDamage()
    {
        health--;
        if(health <= 0)
        {
            isAlive = false;
        }
    }

    [ClientRpc]
    public void RpcDeclareVictory()
    {
        Debug.Log($"{gameObject.name} ha ganado la partida");
    }
}
