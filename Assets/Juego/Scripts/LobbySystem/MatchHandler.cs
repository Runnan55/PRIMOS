using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;
using Mirror;
using System.Collections;
using UnityEngine.SceneManagement;

public class MatchHandler : NetworkBehaviour
{
    public static MatchHandler Instance { get; private set; }

    private Dictionary<string, MatchInfo> matches = new Dictionary<string, MatchInfo>();
    private int partidasActivas = 0;
    public int PartidasActivas => partidasActivas;

    [SerializeField] public GameManager gameManagerPrefab;

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
    public MatchInfo GetMatchInfoByScene(Scene scene)
    {
        foreach (var match in matches.Values)
        {
            if (match.sceneName == scene.name)
                return match;
        }

        return null;
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
        player.TargetForceReturnToLobby(player.connectionToClient);
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

    public bool AreAllPlayersReadyToStart(string matchId)
    {
        if (!matches.TryGetValue(matchId, out MatchInfo match)) return false;

        foreach (var player in match.players)
        {
            if (!player.isPlayingNow)
                return false;
        }
        return true;
    }
    public bool AreAllPlayersReady(string matchId)
    {
        if (!matches.TryGetValue(matchId, out MatchInfo match)) return false;

        foreach (var player in match.players)
        {
            if (!player.isReady)
                return false;
        }
        return true;
    }

    public void CheckStartGame(string matchId)
    {
        if (!matches.ContainsKey(matchId)) return;

        MatchInfo match = matches[matchId];
        if (match.isStarted) return;

        if (AreAllPlayersReady(matchId))
        {
            Debug.Log($"[SERVER] Todos los jugadores listos en {matchId}, empezando partida.");
            match.isStarted = true;

            StartCoroutine(CreateRuntimeGameScene(match));
        }
    }

    [Server]
    private IEnumerator CreateRuntimeGameScene(MatchInfo match)
    {
        string newSceneName = "GameScene_" + match.matchId;
        Scene newScene = SceneManager.CreateScene(newSceneName);

        yield return null; // Esperar un frame para asegurarnos que la escena esté lista

        // Cargar decorado base desde Resources
        GameObject template = Resources.Load<GameObject>("GameSceneTemplate");
        if (template == null)
        {
            Debug.LogError("[MatchHandler] No se encontró el prefab GameSceneTemplate en Resources.");
            yield break;
        }

        GameObject templateInstance = Instantiate(template);
        SceneManager.MoveGameObjectToScene(templateInstance, newScene);

        // Mover todos los jugadores a la escena recién creada
        foreach (var player in match.players)
        {
            SceneManager.MoveGameObjectToScene(player.gameObject, newScene);

            var identity = player.GetComponent<NetworkIdentity>();
            if (identity != null)
            {
                identity.sceneId = 0;
                NetworkServer.RebuildObservers(identity, true);
            }

            player.isPlayingNow = true;

            Debug.Log($"[MatchHandler] Player {player.name} está ahora en escena: {player.gameObject.scene.name}");
        }

        match.sceneName = newScene.name;
        Debug.Log($"[MatchHandler] Escena creada para partida {match.matchId}: {newScene.name}");

        // Notificar a los clientes para que carguen GameScene
        NotifyPlayersToLoadGameScene(match.matchId);
        TrySpawnGameManagerForMatch(match.matchId);
    }

    [Server]
    public void TrySpawnGameManagerForMatch(string matchId)
    {
        if (!matches.TryGetValue(matchId, out MatchInfo match)) return;

        Scene matchScene = SceneManager.GetSceneByName(match.sceneName);
        if (!matchScene.IsValid()) return;

        GameManager gmInstance = Instantiate(gameManagerPrefab);
        gmInstance.matchId = match.matchId;
        gmInstance.playerControllerPrefab = NetworkManager.singleton.playerPrefab;

        NetworkServer.Spawn(gmInstance.gameObject);
        SceneManager.MoveGameObjectToScene(gmInstance.gameObject, matchScene);

        Debug.Log($"[MatchHandler] GameManager spawn para {match.matchId}");
    }


    public bool AreAllPlayersInGameScene(string matchId)
    {
        if (!matches.TryGetValue(matchId, out MatchInfo match)) return false;

        foreach (var player in match.players)
        {
            if (player.gameObject.scene.name != "GameScene")
                return false;
        }
        return true;
    }

    [Server]
    public void NotifyPlayersToLoadGameScene(string matchId)
    {
        if (!matches.TryGetValue(matchId, out MatchInfo match)) return;

        foreach (var player in match.players)
        {
            // ENVÍA EL NOMBRE REAL DE LA ESCENA
            player.TargetStartGame(player.connectionToClient, match.sceneName);
        }
    }
}
