using System.Collections;
using Mirror;
using UnityEngine;

public class FocusResync : NetworkBehaviour
{
    [SerializeField] float debounceSeconds = 1.0f;
    float nextAllowed;
    CustomRoomPlayer room;

    void Awake()
    {
        // Desactivado para todos por defecto
        enabled = false;
        room = GetComponent<CustomRoomPlayer>();
    }

    public override void OnStartLocalPlayer()
    {
        // Sólo el dueño local lo ejecuta
        enabled = true;
    }

    // Cuando vuelve el foco a la pestaña
    [ClientCallback]
    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) TryResync();
    }

    // Cuando “despausa” (WebGL vuelve a foreground)
    [ClientCallback]
    void OnApplicationPause(bool paused)
    {
        if (!paused) TryResync();
    }

    void TryResync()
    {
        if (!isOwned || !NetworkClient.isConnected) return;
        if (room == null || !room.isPlayingNow) return;                 // evita lobby/carga
        if (string.IsNullOrEmpty(room.currentMatchId)) return;          // evita ids vacíos
        if (Time.unscaledTime < nextAllowed) return;

        nextAllowed = Time.unscaledTime + debounceSeconds;
        StartCoroutine(DeferredResync());
    }

    IEnumerator DeferredResync()
    {
        yield return null;
        yield return new WaitForSeconds(0.1f);   // aire al volver del background

        if (!Mirror.NetworkClient.ready)
            Mirror.NetworkClient.Ready();        // cliente en Ready

        room.CmdRegisterSceneInterest(room.currentMatchId); // <-- NUEVO: asegura mapeo server-side
        room.CmdRequestResyncObservers();                   // rebuild observers de tu partida
    }
}
