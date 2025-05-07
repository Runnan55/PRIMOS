using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.InputSystem.XR.Haptics;

public class RolesManager : NetworkBehaviour
{
    public static RolesManager Instance {  get; private set; }

    [SerializeField] private int ParcaKillRequirement = 2;
    [SerializeField] private float ParcaRewardProbability = 0.9f ;

    private PlayerController currentParca = null;

    private Dictionary<PlayerController, int> playerKills = new Dictionary<PlayerController, int>();

       /* private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }*/

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

        Debug.Log($"{killer.gameObject.name} ha matado a {victim.gameObject.name}. Total de kills: {playerKills[killer]}");

        //Si el killer ya es la Parca, curarlo 1 de vida
        if (currentParca == killer)
        {
            killer.ServerHeal(1);
            Debug.Log($"{killer.gameObject.name} es la PARCA y se curó 1 vida por matar.");
            killer.RpcSendLogToClients($"{killer.gameObject.name} se curó 1 vida por ser la PARCA y matar.");
        }

        TryAssignParcaRole();
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
        else
        {
            Debug.Log("Nadie obtuvo el rol Parca esta vez.");
        }
    }

    [Server]
    private void AssignParcaRole(PlayerController newParca, bool firstParca = true)
    {
        currentParca = newParca;
        newParca.RpcSetParcaSprite(true);
        newParca.ammo += 1;

        if (firstParca)
        {
            newParca.ServerHeal(1);
            Debug.Log($"{newParca.gameObject.name} ha obtenido un rol de PARCA y se ha CURADO SOLO 1 VIDA.");
            newParca.RpcSendLogToClients($"{newParca.gameObject.name} ha obtenido el rol PARCA y se ha CURADO SOLO 1 VIDA");
        }
        else
        {
            newParca.ServerHealFull();
            Debug.Log($"{newParca.gameObject.name} ha robado un rol PARCA y se ha CURADO TOTALMENTE.");
            newParca.RpcSendLogToClients($"{newParca.gameObject.name} ha obtenido el rol PARCA y se ha CURADO TOTALMENTE");
        }
        
    }


    [Server]
    public void TransferParcaRole(PlayerController newParca, PlayerController oldParca)
    {
        if (currentParca != oldParca) return;// Asegurar que la Parca actual es la que muere

        // Quitar el rol de la Parca anterior
        oldParca.RpcSetParcaSprite(false);
        currentParca = null;

        Debug.Log($"{oldParca.gameObject.name} ha perdido el rol PARCA");

        // Si el asesino califica, hereda el rol
        if (playerKills.ContainsKey(newParca) && playerKills[newParca] >= ParcaKillRequirement)
        {
            AssignParcaRole(newParca, false);
        }
        else
        {
            Debug.Log("No hay un candidato claro para Parca. Se reiniciará el ciclo.");
        }
    }

}
