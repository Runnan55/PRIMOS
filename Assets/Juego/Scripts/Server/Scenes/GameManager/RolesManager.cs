using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class RolesManager : NetworkBehaviour
{
    [SerializeField] private int ParcaKillRequirement;
    [SerializeField] private float ParcaRewardProbability;

    private PlayerController currentParca = null;

    private Dictionary<PlayerController, int> playerKills = new Dictionary<PlayerController, int>();

    [Server]
    public void RegisterKill(PlayerController killer, PlayerController victim)
    {
        if (!killer || !victim) return;

        if (!playerKills.ContainsKey(killer))
        {
            playerKills[killer] = 0;
        }

        playerKills[killer]++;
        killer.kills = playerKills[killer];//Actualizar contador de kills de jugador

        //Si el asesinado era la Parca actual, transfiere el rol inmediatamente
        if (currentParca == victim)
        {
            TransferParcaRole(killer, victim);
        }

        //Si el killer ya es la Parca, curarlo 1 de vida
        if (currentParca == killer)
        {
            killer.ServerHeal(1);
        }

        TryAssignParcaRole();

        //Actualizar stats después de matar
        GameStatistic stats = GameObject.FindFirstObjectByType<GameStatistic>();
        if (stats != null)
        {
            stats.UpdatePlayerStats(killer);
        }
    }

    [Server]
    private void TryAssignParcaRole()
    {
        if (currentParca != null) return; //Si hay Parca, no se asignan más

        List<PlayerController> potentialParcas = new List<PlayerController>();

        foreach (var entry in playerKills) //Buscar jugadores con 2 kills o más
        {
            if (entry.Value >= ParcaKillRequirement)
            {
                potentialParcas.Add(entry.Key);
            }
        }

        if (potentialParcas.Count == 0) return; //Nadie cumple los requisitos

        //Si hay más de un candidato, elegir al azar
        PlayerController selectedParca = potentialParcas.Count == 1
            ? potentialParcas[0]
            : potentialParcas[Random.Range(0, potentialParcas.Count)];

        // Verificar probabilidad antes de asignar el rol
        if (Random.value <= ParcaRewardProbability)
        {
            AssignParcaRole(selectedParca, true);
        }
    }

    [Server]
    private void AssignParcaRole(PlayerController newParca, bool firstParca = true)
    {
        currentParca = newParca;
        newParca.isParca = true;
        //newParca.RpcSetParcaSprite(true);
        newParca.ammo += 2;

        if (firstParca)
        {
            newParca.ServerHeal(1);
        }
        else
        {
            newParca.ServerHealFull();
        }
    }


    [Server]
    public void TransferParcaRole(PlayerController newParca, PlayerController oldParca)
    {
        if (currentParca != oldParca) return;// Asegurar que la Parca actual es la que muere

        // Quitar el rol de la Parca anterior
        //oldParca.RpcSetParcaSprite(false);
        oldParca.isParca = false;
        currentParca = null;

        AssignParcaRole(newParca, false);

        // Si el asesino califica, hereda el rol
        if (playerKills.ContainsKey(newParca) && playerKills[newParca] >= ParcaKillRequirement)
        {
            AssignParcaRole(newParca, false);
        }
    }
}
