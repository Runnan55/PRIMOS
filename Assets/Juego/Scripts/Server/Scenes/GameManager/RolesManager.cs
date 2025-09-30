using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class RolesManager : NetworkBehaviour
{
    [SerializeField] private int ParcaKillRequirement;
    [SerializeField] private float ParcaRewardProbability;

    [SerializeField] private GameManager gameManager;
    [SerializeField] private GameStatistic gameStatistic;

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
            //TransferParcaRole(killer, victim);
            if (gameManager  != null)
            {
                gameManager.EnqueueParcaTransfer(killer, victim);
            }
        }

        //Si el killer ya es la Parca, curarlo 1 de vida
        if (currentParca == killer)
        {
            killer.ServerHeal(1);
        }

        TryAssignParcaRole(killer);

        //Actualizar stats después de matar
        if (gameStatistic != null)
        {
            gameStatistic.UpdatePlayerStats(killer);
        }
    }

    [Server]
    private void TryAssignParcaRole(PlayerController killer)
    {
        if (currentParca != null) return; //Si hay Parca, no se asignan más
        if (killer == null) return;

        // Requisito: 2+ kills
        if (!playerKills.TryGetValue(killer, out int k) || k < ParcaKillRequirement)
            return;

        // Probabilidad: 70% por kill a partir de la segunda
        // Verificar probabilidad antes de asignar el rol
        if (Random.value <= ParcaRewardProbability)
        {
            AssignParcaRole(killer, true);
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
    }
}
