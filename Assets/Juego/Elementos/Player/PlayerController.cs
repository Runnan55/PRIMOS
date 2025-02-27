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

    public ActionType selectedAction = ActionType.None;

    private void Start()
    {
        if (isLocalPlayer)
        {
            GameManager.Instance.SetLocalPlayer(this);
        }
    }
    public void StartTurn()
    {
        if (!isLocalPlayer) return; // Solo ejecuta esto en el jugador local
        if (!isAlive) return;

        isCovering = false; // Reinicia la cobertura al inicio del turno
        Debug.Log($"{gameObject.name} ha comenzado su turno.");
    }


    [Server]
    public void AttemptShoot(PlayerController target)
    {
        if (ammo <= 0 || target == null || !target.isAlive || target.isCovering) return;

        ammo--;
        target.TakeDamage();
        Debug.Log($"{gameObject.name} disparó a {target.gameObject.name}. Balas restantes: {ammo}");
    }

    [Server]
    public void Reload()
    {
        ammo++;
        Debug.Log($"{gameObject.name} recargó. Balas actuales: {ammo}");
    }

    [Server]
    public void Cover()
    {
        isCovering = true;
        Debug.Log($"{gameObject.name} se cubrió.");
    }

    [Server]
    public void TakeDamage()
    {
        health--;
        Debug.Log($"{gameObject.name} recibió daño. Vida restante: {health}");
        if (health <= 0)
        {
            isAlive = false;
            Debug.Log($"{gameObject.name} ha sido eliminado.");
        }
    }

    private void OnMouseDown()
    {
        if (isLocalPlayer)
        {
            GameManager.Instance.SelectTarget(this);
        }
    }

    [ClientRpc]
    public void RpcDeclareVictory()
    {
        Debug.Log($"{gameObject.name} ha ganado la partida.");
    }
}

