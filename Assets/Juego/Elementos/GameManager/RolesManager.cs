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
        killer.kills = playerKills[killer];//Actualizar contador de kills de jugador

        Debug.Log($"{killer.gameObject.name} ha matado a {victim.gameObject.name}. Total de kills: {playerKills[killer]}");

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
            AssignParcaRole(selectedParca);
        }
        else
        {
            Debug.Log("Nadie obtuvo el rol Parca esta vez.");
        }
    }

    [Server]
    private void AssignParcaRole(PlayerController newParca)
    {
        currentParca = newParca;
        hasParcaRole[newParca] = true;
        newParca.ServerHealFull();
        newParca.RpcSetParcaSprite(true);

        Debug.Log($"{newParca.gameObject.name} ha obtenido el rol PARCA");
        newParca.RpcSendLogToClients($"{newParca.gameObject.name} ha obtenido el rol PARCA y se ha curado completamente");
    }


    [Server]
    public void TransferParcaRole(PlayerController newParca, PlayerController oldParca)
    {
        if (currentParca != oldParca) return;// Asegurar que la Parca actual es la que muere

        // Quitar el rol de la Parca anterior
        hasParcaRole[oldParca] = false;
        oldParca.RpcSetParcaSprite(false);
        currentParca = null;

        Debug.Log($"{oldParca.gameObject.name} ha perdido el rol PARCA");

        // Si el asesino califica, hereda el rol
        if (playerKills.ContainsKey(newParca) && playerKills[newParca] >= ParcaKillRequirement)
        {
            AssignParcaRole(newParca);
        }
        else
        {
            Debug.Log("No hay un candidato claro para Parca. Se reiniciará el ciclo.");
        }
    }

}
