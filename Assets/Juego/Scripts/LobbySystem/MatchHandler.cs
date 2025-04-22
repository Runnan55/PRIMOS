using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;
using Mirror;

public class MatchHandler : NetworkBehaviour
{
    public static MatchHandler Instance { get; private set; }

    private Dictionary<string, MatchInfo> matches = new Dictionary<string, MatchInfo>();
    private int partidasActivas = 0;
    public int PartidasActivas => partidasActivas;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public bool CreateMatch(string matchId, string mode, CustomRoomPlayer creator)
    {
        if (matches.ContainsKey(matchId)) return false;

        MatchInfo newMatch = new MatchInfo
        {
            matchId = matchId,
            mode = mode,
            admin = creator,
            sceneName = "Room_" + matchId
        };
        newMatch.players.Add(creator);

        matches.Add(matchId, newMatch);
        partidasActivas++;

        Debug.Log($"[SERVER] Nueva partida creada. Total partidas activas: {partidasActivas}");

        creator.currentMatchId = matchId;
        creator.currentMode = mode;   // Agrega esto para que se sincronice al cliente
        creator.isAdmin = true;

        creator.RpcRefreshLobbyForAll();
        RpcRefreshMatchList();

        return true;
    }


    public bool JoinMatch(string matchId, CustomRoomPlayer player)
    {
        if (!matches.ContainsKey(matchId)) return false;

        MatchInfo match = matches[matchId];
        match.players.Add(player);

        player.currentMatchId = matchId;
        player.currentMode = match.mode; // Agrega esto para que al unirse también sepa el modo
        player.isAdmin = false;

        player.RpcRefreshLobbyForAll();
        RpcRefreshMatchList();

        return true;
    }


    public void LeaveMatch(CustomRoomPlayer player)
    {
        if (string.IsNullOrEmpty(player.currentMatchId)) return;

        MatchInfo match = matches[player.currentMatchId];
        match.players.Remove(player);

        if (match.players.Count == 0)
        {
            matches.Remove(player.currentMatchId);
            partidasActivas--; // <--- restamos
            Debug.Log($"[SERVER] Partida eliminada. Total partidas activas: {partidasActivas}");
        }
        else
        {
            // Si el Admin se va, asignar nuevo Admin
            if (match.admin == player)
            {
                match.admin = match.players[0];
                match.admin.isAdmin = true;

                // Informar a todos que el Admin cambió
                foreach (var p in match.players)
                {
                    p.TargetUpdateAdmin(match.admin.playerId);
                }
            }

            // Opcional: Actualizar la UI para los que quedan
            foreach (var p in match.players)
            {
                p.RpcRefreshLobbyForAll();
            }
        }

        player.currentMatchId = null;
        player.isAdmin = false;
    }

    [ClientRpc]
    public void RpcRefreshMatchList()
    {
        if (!isClientOnly) return; //Evitar que se llame en el server

        LobbyUIManager ui = FindFirstObjectByType<LobbyUIManager>();
        if (ui != null)
        {
            ui.RequestMatchList(); // Pedimos actualizar lista
        }
    }


    public List<MatchInfo> GetMatches(string mode)
    {
        List<MatchInfo> filteredMatches = new List<MatchInfo>();
        foreach (var match in matches.Values)
        {
            if (match.mode == mode && !match.isStarted)
            {
                filteredMatches.Add(match);
            }
        }

        return filteredMatches;
    }

    public MatchInfo GetMatch(string matchId)
    {
        if (matches.ContainsKey(matchId))
            return matches[matchId];
        else
            return null;
    }

    public bool AreAllPlayersReady(string matchId)
    {
        if (matches.TryGetValue(matchId, out MatchInfo match))
        {
            foreach (var player in match.players)
            {
                if (!player.isReady)
                    return false;
            }
            return true;
        }
        return false;
    }

    public void CheckStartGame(string matchId)
    {
        if (!matches.ContainsKey(matchId))
            return;

        MatchInfo match = matches[matchId];

        // Si partida ya empezó, no hacer nada
        if (match.isStarted)
            return;

        // Si todos están listos
        if (AreAllPlayersReady(matchId))
        {
            Debug.Log($"[SERVER] Todos los jugadores listos en {matchId}, empezando partida.");

            match.isStarted = true;

            foreach (var player in match.players)
            {
                player.TargetStartGame(player.connectionToClient, match.mode);
            }
        }
    }

}
