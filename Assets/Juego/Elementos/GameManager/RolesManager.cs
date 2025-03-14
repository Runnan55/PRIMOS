using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.InputSystem.XR.Haptics;

public class RolesManager : NetworkBehaviour
{
    public static RolesManager Instance {  get; private set; }

    private Dictionary<PlayerController, int> playerKills = new Dictionary<PlayerController, int>();
    private Dictionary<PlayerController, bool> hasParcaRole = new Dictionary<PlayerController, bool>();

        private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }

    [Server]
    public void RegisterKill(PlayerController killer, PlayerController victim)
    {
        if (!killer || !victim) return;

        if (!playerKills.ContainsKey(killer))
        {
            playerKills[killer] = 0;
        }

        playerKills[killer]++;
        Debug.Log($"{killer.gameObject.name} ha matado a {victim.gameObject.name}. Total de kills: {playerKills[killer]}");

        if (playerKills[killer] == 1)
        {
            float chance = Random.value;
            if (chance <= 0.9f) //90% probabilidad de recibir el rol
            {
                hasParcaRole[killer] = true;
                Debug.Log($"{killer.gameObject.name} ha obtenido el rol de Parca");
                killer.RpcSendLogToClients($"{killer.gameObject.name} ha obtenido el rol de Parca");
            }
        }

        if (hasParcaRole.ContainsKey(killer) && hasParcaRole[killer])
        {
            killer.ServerHeal(1);
            Debug.Log($"{killer.gameObject.name} se ha curado 1 de vida por su rol PARCA");
        }
    }

    [Server]
    public void TransferParcaRole(PlayerController newParca, PlayerController oldParca)
    {
        if (hasParcaRole.ContainsKey(oldParca) && hasParcaRole[oldParca])
        {
            hasParcaRole[oldParca] = false;
            oldParca.RpcSetParcaSprite(false);

            Debug.Log($"{oldParca.gameObject.name} ha perdido el rol PARCA");
        }

        hasParcaRole[newParca] = true;
        newParca.ServerHealFull();
        newParca.RpcSetParcaSprite(true);

        Debug.Log($"{newParca.gameObject.name} ha obtenido el rol PARCA y se ha curado completamente");
        newParca.RpcSendLogToClients($"{newParca.gameObject.name} ha obtenido el rol PARCA y se ha curado completamente");
    }

}
