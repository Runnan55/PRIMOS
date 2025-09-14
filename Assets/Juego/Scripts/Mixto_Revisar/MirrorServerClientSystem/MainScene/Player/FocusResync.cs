// FocusResync.cs (with robust scene resolution and verbose logs)
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class FocusResync : NetworkBehaviour
{
    [Header("Focus debounce (seconds)")]
    [SerializeField] private float debounceSeconds = 1.0f;

    private float nextAllowed;
    private CustomRoomPlayer room;

    // expected snapshot from server (already filtered to the real match scene)
    private static HashSet<uint> expectedIds = new HashSet<uint>();
    private static int expectedPlayers = 0;
    private static int snapshotSerial = 0; // increases when snapshot arrives

    // Called by CustomRoomPlayer.TargetReceiveExpectedSnapshot
    public static void SetExpectedSnapshot(uint[] ids, int players)
    {
        expectedIds.Clear();
        if (ids != null) for (int i = 0; i < ids.Length; i++) expectedIds.Add(ids[i]);
        expectedPlayers = Mathf.Max(players, 0);
        snapshotSerial++;
        Debug.Log($"[FOCUS] <recv> snapshot ids={expectedIds.Count} players={expectedPlayers} serial={snapshotSerial}");
    }

    private void Awake()
    {
        enabled = false; // only local player runs this
        room = GetComponent<CustomRoomPlayer>();
    }

    public override void OnStartLocalPlayer()
    {
        enabled = true;
        Debug.Log("[FOCUS] enabled for local player");
    }

    [ClientCallback]
    private void OnApplicationFocus(bool hasFocus)
    {
        Debug.Log($"[FOCUS] OnApplicationFocus hasFocus={hasFocus}");
        if (hasFocus) TryResync();
    }

    [ClientCallback]
    private void OnApplicationPause(bool paused)
    {
        Debug.Log($"[FOCUS] OnApplicationPause paused={paused}");
        if (!paused) TryResync();
    }

    public void Kick() => TryResync(); // optional manual trigger

    private void TryResync()
    {
        Debug.Log($"[FOCUS] TryResync t={Time.unscaledTime:F2}");
        if (!isOwned || !NetworkClient.isConnected) { Debug.Log("[FOCUS] skip: not owned/connected"); return; }
        if (room == null || !room.isPlayingNow) { Debug.Log("[FOCUS] skip: not playing"); return; }
        if (string.IsNullOrEmpty(room.currentMatchId)) { Debug.Log("[FOCUS] skip: no matchId"); return; }
        if (Time.unscaledTime < nextAllowed) { Debug.Log("[FOCUS] skip: debounce"); return; }
        nextAllowed = Time.unscaledTime + debounceSeconds;
        StartCoroutine(ResyncFlow());
    }

    private IEnumerator ResyncFlow()
    {
        Debug.Log("[FOCUS] flow start");
        if (!NetworkClient.ready) { Debug.Log("[FOCUS] calling NetworkClient.Ready()"); NetworkClient.Ready(); }
        if (room != null && !string.IsNullOrEmpty(room.currentSceneName))
        {
            Debug.Log($"[FOCUS] CmdRegisterSceneInterest scene={room.currentSceneName}");
            room.CmdRegisterSceneInterest(room.currentSceneName);
        }
        yield return null;

        int before = snapshotSerial;
        Debug.Log("[FOCUS] CmdRequestExpectedSnapshot...");
        room.CmdRequestExpectedSnapshot();

        float t0 = Time.realtimeSinceStartup, timeout = 1.0f;
        while (snapshotSerial == before && (Time.realtimeSinceStartup - t0) < timeout) yield return null;

        if (snapshotSerial == before) Debug.LogWarning("[FOCUS] snapshot timeout, proceeding without it");
        else Debug.Log($"[FOCUS] snapshot received after {(Time.realtimeSinceStartup - t0):F3}s");

        bool needSweep = DetectMissingAndCollect(out var missingIds);

        if (!needSweep)
        {
            Debug.Log("[FOCUS] Already in sync -> skipping CmdRequestResyncObservers");
            nextAllowed = Time.unscaledTime + 3.0f; // cool-off
            yield break;
        }

        Debug.Log($"[FOCUS] CmdRequestResyncObservers needSweep={needSweep} missing={missingIds.Length}");
        room.CmdRequestResyncObservers(missingIds, needSweep);
    }

    private static readonly List<uint> tmpMissing = new List<uint>();

    private bool DetectMissingAndCollect(out uint[] missingIds)
    {
        tmpMissing.Clear();

        Scene scene = ResolveClientGameScene(out string why);
        Debug.Log($"[FOCUS] ResolveClientGameScene -> {scene.name} ({why})");
        if (!scene.IsValid())
        {
            missingIds = System.Array.Empty<uint>();
            Debug.LogWarning("[FOCUS] invalid scene -> force sweep");
            return true;
        }

        int havePlayers = CountPlayersInScene(scene);
        bool playersMissing = expectedPlayers > 0 && havePlayers < expectedPlayers;

        bool anyIdMissing = false;
        if (expectedIds != null && expectedIds.Count > 0)
        {
            foreach (var id in expectedIds)
            {
                if (!NetworkClient.spawned.ContainsKey(id))
                {
                    anyIdMissing = true;
                    tmpMissing.Add(id);
                    Debug.LogWarning($"[FOCUS] missing netId={id}");
                }
            }
        }

        Debug.Log($"[FOCUS] check(players-only): {havePlayers}/{expectedPlayers} missPlayers={playersMissing} idsMissing={anyIdMissing} missingCount={tmpMissing.Count}");
        missingIds = tmpMissing.ToArray();
        return playersMissing || anyIdMissing;
    }

    // Prefer the client template "GameScene" if the real one is not present on client
    private Scene ResolveClientGameScene(out string reason)
    {
        if (room != null && !string.IsNullOrEmpty(room.currentSceneName))
        {
            var real = SceneManager.GetSceneByName(room.currentSceneName);
            if (real.IsValid()) { reason = "real-scene"; return real; }
        }
        var template = SceneManager.GetSceneByName("GameScene");
        if (template.IsValid()) { reason = "template-GameScene"; return template; }

        var active = SceneManager.GetActiveScene();
        if (active.IsValid()) { reason = "activeScene"; return active; }

        reason = "fallback-DontDestroyOnLoad";
        return gameObject.scene;
    }

    private int CountPlayersInScene(Scene scene)
    {
        int count = 0;
        foreach (var kv in NetworkClient.spawned)
        {
            var ni = kv.Value;
            if (ni == null) continue;
            if (ni.gameObject.scene != scene) continue;
            if (ni.GetComponent<PlayerController>() != null) count++;
        }
        Debug.Log($"[FOCUS] CountPlayersInScene scene={scene.name} -> {count}");
        return count;
    }

    private bool SceneHasComponent<T>(Scene scene) where T : Component
    {
        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            if (roots[i].GetComponentInChildren<T>(true) != null) return true;
        return false;
    }
}