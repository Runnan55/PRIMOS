﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework.Constraints;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum ActionType
{
    None, //Opción por si el jugador no elige nada
    Shoot,
    Reload,
    Cover,
    SuperShoot
}

public class PlayerAction
{
    public ActionType type;
    public PlayerController target; //Se usa solo para el disparo

    public PlayerAction(ActionType type, PlayerController target = null)
    {
        this.type = type;
        this.target = target;
    }
}

public class PlayerController : NetworkBehaviour
{
    public bool isAlive = true;
    [SyncVar] public PlayerController lastShotTarget = null;
    [SyncVar(hook = nameof(OnAmmoChanged))] public int ammo;
    [SyncVar(hook = nameof(OnHealthChanged))] public int health; private int fullHealth; //Esta vida no es variable es para saber cual es la vida máxima
    [SyncVar(hook = nameof(OnNameConfirmedChanged))] public bool hasConfirmedName = false; //Decir al server que el jugador eligió nombre
    [SyncVar(hook = nameof(OnNameChanged))] public string playerName;
    [SyncVar(hook = nameof(OnKillsChanged))] public int kills = 0;

    [SyncVar] public bool clientDecisionPhase;
    [SyncVar] public bool isCovering = false;
    [SerializeField] private int minBulletS = 1;
    [SerializeField] private int minBulletSS = 3;
    [SerializeField] public float[] coverProbabilities = { 1f, 0.5f, 0.01f };//Probabilidades 100%, 50%, 1%
    [SyncVar(hook = nameof (OnCoverLevelChanged))] public int consecutiveCovers = 0;

    private static PlayerController targetEnemy; //Enemigo seleccionado
    private static bool isAiming = false; //Indica si se está apuntando

    private Animator animator;

    public ActionType selectedAction = ActionType.None;

    [Header("UI Elements")]
    public GameObject playerCanvas;
    public GameObject deathCanvas;
    public GameObject victoryCanvas;
    public GameObject drawCanvas;
    public GameObject targetIndicator; //Indicador visual de objetivo elegido
    public GameObject localPlayerIndicator; //Indicador visual de tu jugador

    [Header("Crosshair")]
    public GameObject crosshairPrefab; //Prefab de la mirilla
    private GameObject crosshairInstance; //Instancia que crea el script cuando seleccionamos disparar

    public TMP_Text healthText;
    public TMP_Text ammoText;
    public TMP_Text coverProbabilityText;
    public TMP_Text countdownText;
    public TMP_Text playerNameText;

    [Header("UI Buttons")]
    public Button shootButton;
    public Button reloadButton;
    public Button coverButton;
    public Button superShootButton;
    private Button selectedButton = null; //Último botón seleccionado
    [SerializeField] private Color defaultButtonColor = Color.white; //Color por defecto
    [SerializeField] private Color highlightedColor = Color.white; //Color resaltado

    [Header("UIButtonExitSettingAudioEtc")]
    public Button exitGameButton;

    [Header("UI Timer/Round")]
    [SyncVar(hook = nameof(OnRoundChanged))] public int syncedRound;
    [SyncVar(hook = nameof(OnTimerChanged))] public float syncedTimer;
    public TMP_Text roundTextUI;
    public TMP_Text timerTextUI;

    [Header("Rol Elements")]
    public GameObject parcaSprite;

    [Header("GameModifierType")]
    public bool isDarkReloadEnabled = false;
    [SyncVar(hook = nameof(OnIsVeryHealthyChanged))] public bool isVeryHealthy = false; //Si está en true (por Cacería de Lider) coverButton no funcionará
    public bool rustyBulletsActive = false; //Balas Oxidadas

    [Header("MisionesRápidas")]
    public QuickMission currentQuickMission = null;
    [SyncVar] public bool wasShotBlockedThisRound = false;
    [SyncVar] public bool hasDoubleDamage = false;
    [SyncVar] public bool shieldBoostActivate = false;
    [SyncVar] public bool hasDamagedAnotherPlayerThisRound = false;
    [SyncVar] public bool hasQMRewardThisRound = false;

    [Header("ShootBulletAnimation")]
    public GameObject projectilePrefab;
    public Transform shootOrigin;

    [Header("GameStatistics")]
    [SyncVar] public int bulletsReloaded = 0;
    [SyncVar] public int bulletsFired = 0;
    [SyncVar] public int damageDealt = 0;
    [SyncVar] public int timesCovered = 0;
    [SyncVar] public int deathOrder = 0;

    [Header("PlayersOrientation")]
    public int playerPosition;
    public GameObject spriteRendererContainer;

    [Header("TikiTalismanEffect")]
    [SyncVar] public bool canDealDamageThisRound = true;
    [SyncVar] public int sucessfulShots = 0;

    [Header("TikiVisualIndicator")]
    public GameObject tikiSprite;

    [Header("Game Roulette Modifier")]
    public GameModifierRoulette roulette;
    public GameObject gameModifierCanvas;

    [SyncVar] public uint gameManagerNetId;
    private GameManager cachedGameManager;

    [TargetRpc]
    public void TargetHideRouletteCanvas(NetworkConnection target)
    {
        if (gameModifierCanvas != null)
            gameModifierCanvas.SetActive(false);
    }

    #region Leaderboard

    [SerializeField] private GameObject leaderboardCanvas;
    [SerializeField] private Transform leaderboardContent;
    [SerializeField] private GameObject leaderboardEntryPrefab;

    [ClientRpc]
    public void RpcShowLeaderboard(List<GameStatistic.PlayerInfo> playerInfos)
    {
        Debug.Log("[PlayerController] Recibido RpcShowLeaderboard, activando canvas local...");

        if (!isOwned) return;

        leaderboardCanvas.SetActive(true); // Activar canvas que ya está en tu prefab
        ClearLeaderboard();

        var ordered = playerInfos.OrderBy(p => p.deathOrder == 0 ? int.MinValue : p.deathOrder).ToList();

        foreach (var player in ordered)
        {
            GameObject entry = Instantiate(leaderboardEntryPrefab, leaderboardContent);
            entry.transform.SetParent(leaderboardContent, false);
            entry.transform.localScale = Vector3.one;

            var texts = entry.GetComponentsInChildren<TMP_Text>();
            if (texts.Length < 7)
            {
                Debug.LogError("[Leaderboard] No se encontraron suficientes TMP_Text en el prefab.");
                continue;
            }

            string displayName = player.playerName;
            if (player.isDisconnected)
            {
                displayName += " (Offline)";
            }

            texts[0].text = displayName;
            texts[1].text = player.kills.ToString();
            texts[2].text = player.bulletsReloaded.ToString();
            texts[3].text = player.bulletsFired.ToString();
            texts[4].text = player.damageDealt.ToString();
            texts[5].text = player.timesCovered.ToString();
            texts[6].text = player.points.ToString();
        }
    }

    private void ClearLeaderboard()
    {
        foreach (Transform child in leaderboardContent)
        {
            Destroy(child.gameObject);
        }
    }

    public void OnExitLeaderboardPressed()
    {
        Debug.Log($"[ExitButton] isOwned: {isOwned}");
        Debug.Log("[PlayerController] Botón de salida del leaderboard presionado.");
        CmdReturnToLobbyScene();
    }

    [Command]
    private void CmdReturnToLobbyScene()
    {
        Debug.Log($"[SERVER] {playerName} quiere volver a LobbySceneCasual");

        // Mover su CustomRoomPlayer
        if (ownerRoomPlayer != null)
        {
            var lobbyScene = SceneManager.GetSceneByName("LobbySceneCasual");
            if (lobbyScene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(ownerRoomPlayer.gameObject, lobbyScene);
                Debug.Log($"[SERVER] {playerName} movido a escena LobbySceneCasual");
            }
        }

        // Cambiar escena del cliente local
        TargetReturnToLobbyScene(connectionToClient);

        // Destruir el PlayerController del servidor
        StartCoroutine (DestroyMe());
    }

    private IEnumerator DestroyMe()
    {
        yield return new WaitForSeconds(1f);
        NetworkServer.Destroy(gameObject);
    }

    [TargetRpc]
    private void TargetReturnToLobbyScene(NetworkConnection target)
    {
        Debug.Log("[CLIENT] Cambiando escena visual a LobbySceneCasual...");
        SceneManager.LoadScene("LobbySceneCasual");
    }

    #endregion
    public GameManager GameManager
    {
        get
        {
            if (cachedGameManager == null && gameManagerNetId != 0)
            {
                if (NetworkServer.spawned.TryGetValue(gameManagerNetId, out var identity))
                {
                    cachedGameManager = identity.GetComponent<GameManager>();
                }
            }
            return cachedGameManager;
        }
    }

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            Debug.Log("GameManager en Cliente inexistente, pero estaba previsto");
        }

        if (!isLocalPlayer)
        {
            Debug.Log("El localPlayer no tiene poder sobre este prefab, pero estaba previsto");
        }

        if (isOwned)
        {
            Debug.Log("El jugador tiene autoridad sobre este playerprefab, pero el Network de Mirror no lo detecta como Local Player, pero está bien");
        }

        if (isLocalPlayer || isOwned)
        {
            shootButton.interactable = (false);
            superShootButton.interactable = (false);
            reloadButton.interactable = (false);
            coverButton.interactable = (false);

            localPlayerIndicator.SetActive(true);

            deathCanvas.SetActive(false);
            victoryCanvas.SetActive(false);
            drawCanvas.SetActive(false);
            targetIndicator.SetActive(false);
            playerCanvas.SetActive(true);
            gameModifierCanvas.SetActive(true);

            /*if (shootButton) shootButton.onClick.AddListener(() => OnShootButton());
            if (reloadButton) reloadButton.onClick.AddListener(() => OnReloadButton());
            if (coverButton) coverButton.onClick.AddListener(() => OnCoverButton());
            if (superShootButton) superShootButton.onClick.AddListener(() => OnSuperShootButton());*/

            AddPointerDownEvent(shootButton, ActionType.Shoot);

            AddPointerDownEvent(reloadButton, ActionType.Reload);

            AddPointerDownEvent(coverButton, ActionType.Cover);

            AddPointerDownEvent(superShootButton, ActionType.SuperShoot);
        }
        else
        {
            playerCanvas.SetActive(false);
            localPlayerIndicator.SetActive(false);
            gameModifierCanvas.SetActive(false);
        }

        fullHealth = health; //guardamos el valor inicial de health

        if (targetIndicator != null)
            targetIndicator.SetActive(false);
    }

    #region ParcheParaQueBotonesSeActivenAlPulsarNoAlLevantarClic

    private void AddPointerDownEvent(Button button, ActionType action)
    {
        if (button == null) return;

        EventTrigger trigger = button.gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = button.gameObject.AddComponent<EventTrigger>();

        var entry = new EventTrigger.Entry();
        entry.eventID = EventTriggerType.PointerDown;
        entry.callback.AddListener((data) => { OnActionPressed(action); });

        trigger.triggers.Add(entry);
    }

    private ActionType lastPressedButton = ActionType.None;

    public void OnActionPressed(ActionType action)
    {
        if (!isOwned || !clientDecisionPhase) return;

        if (lastPressedButton == action)
        {
            CancelCurrentAction();
            lastPressedButton = ActionType.None;
            return;
        }

        lastPressedButton = action;

        switch (action)
        {
            case ActionType.Shoot:
                StartCoroutine(DelayedShoot()); break;
            case ActionType.SuperShoot:
                StartCoroutine(DelayedSuperShoot()); break;
            case ActionType.Reload:
                OnReloadButton(); break;
            case ActionType.Cover:
                OnCoverButton(); break;
        }
    }

    private void CancelCurrentAction()
    {
        selectedAction = ActionType.None;
        lastPressedButton = ActionType.None;
        isAiming = false;

        if (crosshairInstance)
        {
            Destroy(crosshairInstance);
            crosshairInstance = null;
        }

        CmdRegisterAction(ActionType.None, null); //Registramos NONE
        ResetButtonHighlightLocally(); // Método local, no RPC
    }

    private void ResetButtonHighlightLocally()
    {
        if (selectedButton != null)
        {
            ColorBlock colors = selectedButton.colors;
            colors.normalColor = defaultButtonColor;
            colors.selectedColor = defaultButtonColor;
            selectedButton.colors = colors;
            selectedButton = null;
        }
    }


    private IEnumerator DelayedShoot()
    {
        yield return null; // espera 1 frame
        OnShootButton();
    }

    private IEnumerator DelayedSuperShoot()
    {
        yield return null; // espera 1 frame
        OnSuperShootButton();
    }


    #endregion

    #region ConectWithClient

    [SyncVar] public string playerId;

    public CustomRoomPlayer ownerRoomPlayer;

    #endregion

    public override void OnStartClient()
    {
        base.OnStartClient();
        OnHealthChanged(health, health); //Forzamos mostrar vida al inicio aunque no cambie
        OnAmmoChanged(ammo, ammo); //Forzamos mostrar munición al inicio aunque no cambie
        //No forzamos un OnNameChanged porque el nombre se actualiza al inicio de la partida

        PlayDirectionalAnimation("Idle");
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        DetectSpawnPosition(transform.position); //Setear orientacion y posición usando la posición actual
    }

    [Client]
    private void Update()
    {
        if (!isOwned) return;

        if (!clientDecisionPhase)
        {
            if (isAiming && crosshairInstance)
            {
                Destroy(crosshairInstance);
                crosshairInstance = null;
            }

            isAiming = false;
            selectedAction = ActionType.None;
            return;
        }

        //Mover mirilla con mouse
        if (isAiming && crosshairInstance)
        {
            Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePosition.z = 0;
            crosshairInstance.transform.position = mousePosition;
        }

        //Detectar clics para seleccionar enemigos o cancelar apuntado
        if (Input.GetMouseButtonDown(0))
        {
            if (!isAiming)
            {
                return; //Evita disparar si no se está apuntando
            }

            RaycastHit2D hit = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero);

            if (hit.collider != null)
            {
                PlayerController clickedPlayer = hit.collider.GetComponent<PlayerController>();

                if (clickedPlayer != null && clickedPlayer != this)
                {
                    //Verifica que la acción seleccionada sea Shoot o SuperShoot antes de registrar la acción
                    if (selectedAction == ActionType.Shoot)
                    {
                        Debug.Log($"Objetivo seleccionado: {clickedPlayer.playerName}");
                        CmdRegisterAction(ActionType.Shoot, clickedPlayer);
                    }
                    if (selectedAction == ActionType.SuperShoot)
                    {
                        Debug.Log($"Objetivo seleccionado: {clickedPlayer.playerName}");
                        CmdRegisterAction(ActionType.SuperShoot, clickedPlayer);
                    }
                    Destroy(crosshairInstance);
                    isAiming = false;
                    selectedAction = ActionType.None;
                    //ResetButtonHighlightLocally();
                }
                else
                {
                    Debug.Log("Clic en objeto no válido, Apuntado cancelado");
                    CancelCurrentAction();
                }
            }
            else
            {
                Debug.Log("Clic en ningún objeto, cancelado por chistoso ;)");
                CancelCurrentAction();
            }
        }
    }
    #region Tiki

    [ClientRpc]
    public void RpcSetTikiHolder(bool isHolder)
    {
        bool shouldShow = isHolder && isAlive;
        if (tikiSprite != null)
        {
            tikiSprite.SetActive(shouldShow);
        }
    }

    #endregion

    #region UI HOOKS

    [Server]
    public void SetAliveState(bool state)
    {
        isAlive = state;
        RpcSyncAliveState(state); // Enviar la actualización a los clientes
    }

    [ClientRpc]
    void RpcSyncAliveState(bool state)
    {
        isAlive = state; // Solo actualizar visualmente si se necesita
    }

    [ClientRpc]
    public void RpcSendLogToClients(string logMessage) //Recibir Debug.Log del server para mostrarlos en la DebugConsole
    {
        Debug.Log(logMessage);
    }

    [ClientRpc]
    public void RpcUpdateCoverProbabilityUI(float updatedProbability)
    {
        if (coverProbabilityText != null)
        {
            coverProbabilityText.text = $"Cobertura\n{(updatedProbability * 100):0}%";
        }
    }

    void OnKillsChanged(int oldValue, int newValue)
    {
        Debug.Log($"{playerName} ahora tiene {newValue} kills.");
    }

    void OnNameConfirmedChanged(bool oldValue, bool newValue) //Indicar al server que este player eligio nombre
    {
        /*if ((isLocalPlayer || isOwned) && newValue)
        {
            canvasInicioPartida.SetActive(false);
        }*/
    }

    void OnNameChanged(string oldName, string newName)
    {
        if (playerNameText != null)
            playerNameText.text = newName;
    }

    public void OnIsVeryHealthyChanged(bool oldValue, bool newValue)
    {
        // Opcional: actualiza visualmente al jugador, muestra íconos, colores, etc.
        Debug.Log($"{playerName}: isVeryHealthy cambió a {newValue}");
    }

    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        if (healthText)
            healthText.text = new string('-', newHealth);
    }

    private void OnAmmoChanged(int oldAmmo, int newAmmo)
    {
        if (ammoText)
            ammoText.text = $"Balas: {newAmmo}";

        SuperShootButtonAnimation(newAmmo);
    }

    #endregion

    #region Timer/RoundUI

    public void OnRoundChanged(int oldRound, int newRound)
    {
        if (roundTextUI != null)
        {
            roundTextUI.text = $"Round: {newRound}";
        }

        // ← Reset de lógica de selección de acción
        selectedAction = ActionType.None;
        lastPressedButton = ActionType.None;
        ResetButtonHighlightLocally();
    }

    private void OnTimerChanged(float oldTimer, float newTimer)
    {
        UpdateTimerUI(newTimer);
    }

    public void UpdateTimerUI(float timeLeft)
    {
        if (timerTextUI != null)
        {
            if (timeLeft > 0)
            {
                int displayTime = Mathf.FloorToInt(timeLeft);
                timerTextUI.text = $"Time: {displayTime}";
            }
            else
            {
                timerTextUI.text = $"Time: -"; // Cambia a guión al llegar a 0 o menos
            }
        }
    }

    #endregion

    #region Animations
    [TargetRpc]
    public void TargetPlayButtonAnimation(NetworkConnection target, bool enableButtons)
    {
        if (!isAlive || !gameObject.activeInHierarchy) return;// Evita ejecutar si el jugador está muerto o desactivado

        // Solo habilitar los botones si enableButtons es true y cumplen con sus respectivas condiciones
        shootButton.interactable = enableButtons && ammo >= minBulletS;
        superShootButton.interactable = enableButtons && ammo >= minBulletSS;
        reloadButton.interactable = enableButtons;

        coverButton.interactable = enableButtons && !isVeryHealthy; //Requiere también que el jugador no tenga el bool de Caceria de Lider activo
    }

    [ClientRpc] //Se ejecuta en todos los clientes siempre
    public void RpcPlayAnimation(string animation)
    {
        if (!isAlive) return;

        GetComponent<NetworkAnimator>().animator.Play(animation);//Esto sirve por ejemplo para que el player llame animación en otro player, tambien se puede llamar desde el Server
    }

    [TargetRpc] //Se ejecuta en solo un cliente especifico
    public void TargetPlayAnimation(string animation)
    {
        GetComponent<Animator>().Play(animation); // SOLO ese jugador lo ve
    }

    [ClientRpc]
    public void RpcPlayShootEffect(Vector3 targetPosition)
    {
        if (projectilePrefab == null || shootOrigin == null) return;

        GameObject proj = Instantiate(projectilePrefab, shootOrigin.position, Quaternion.identity);

        //Calcular la dirección hacia el objetivo
        Vector3 direction = (targetPosition - shootOrigin.position).normalized;

        //Rotar la bala para que mire hacia el objetivo (2D, solo rotación en Z)
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        proj.transform.rotation = Quaternion.Euler(new Vector3(0, 0, angle));

        StartCoroutine(MoveProjectileToTarget(proj, targetPosition));
    }

    private IEnumerator MoveProjectileToTarget(GameObject projectile, Vector3 targetPos)
    {
        float duration = 1f;
        float elapsed = 0f;

        Vector3 start = projectile.transform.position;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            projectile.transform.position = Vector3.Lerp(start, targetPos, t);
            yield return null;
        }

        Destroy(projectile);
    }

    #endregion

    #region ButtonFunnyAnimations

    private void OnCoverLevelChanged(int oldValue, int newValue)
    {
        CoverButtonAnimation(newValue);
    }

    private int lastCoverLevel = -1;

    private void CoverButtonAnimation(int coverLevel)
    {
        //if (!isLocalPlayer && !isOwned) return;
        if (!isServer) return; // Solo el servidor manda la animación

        coverLevel = Mathf.Clamp(coverLevel, 0, 2); // 0 = 100%%, 1 = 50%, 2 = 1%
        if (coverLevel == lastCoverLevel) return;

        string animName = coverLevel switch
        {
            0 => "Cover100",
            1 => "Cover50",
            2 => "Cover1",
            _ => null
        };

        if (!string.IsNullOrEmpty(animName))
        {
            GetComponent<Animator>().Play(animName);
            lastCoverLevel = coverLevel;
        }
    }

    private int lastSuperShootLevel = -1;

    private void SuperShootButtonAnimation(int currentAmmo)
    {
        //if (!isLocalPlayer && !isOwned) return;
        if (!isServer) return;

        int superShootLevel = Mathf.Clamp(currentAmmo, 0, 3);

        if (superShootLevel == lastSuperShootLevel) return; // Solo reproducir si hay cambio de estado (evita volver a animar innecesariamente)
        if (superShootLevel == 3 && lastSuperShootLevel >= 3) return; // No reproducir si el anterior era más de 3 tmb

        string animName = superShootLevel switch
        {
            0 => "SuperShoot0",
            1 => "SuperShoot1",
            2 => "SuperShoot2",
            3 => "SuperShoot3",
            _ => null
        };

        if (!string.IsNullOrEmpty(animName))
        {
            GetComponent<Animator>().Play(animName);
            lastSuperShootLevel = superShootLevel;
        }
    }

    #endregion

    #region Roles

    [ClientRpc]
    public void RpcSetParcaSprite(bool isActive)
    {
        if (parcaSprite != null)
        {
            parcaSprite.SetActive(isActive);
        }
    }

    #endregion

    //Forzar sincronización inicial, sino no se verán los datos de los otros jugadores al inicio
    #region SetPosition&AnimationFlip

    public void DetectSpawnPosition(Vector3 pos)
    {
        float x = pos.x;
        float y = pos.y;

        if (x < 0 && y < 0)
            playerPosition = 1;
        else if (x < 0 && Mathf.Approximately(y, 0))
            playerPosition = 2;
        else if (x < 0 && y > 0)
            playerPosition = 3;
        else if (x > 0 && y > 0)
            playerPosition = 4;
        else if (x > 0 && Mathf.Approximately(y, 0))
            playerPosition = 5;
        else if (x > 0 && y < 0)
            playerPosition = 6;

        UpdateFacingDirectionFromPosition();
        ApplyVisualFlipFromDirection();
    }

    [SyncVar(hook = nameof(OnDirectionChanged))]
    public FacingDirection currentFacingDirection;

    private void ApplyVisualFlipFromDirection()
    {
        if (spriteRendererContainer == null) return;

        bool flipX = currentFacingDirection switch
        {
            FacingDirection.Left => true,
            FacingDirection.UpLeft => true,
            FacingDirection.DownLeft => true,
            _ => false
        };

        Vector3 scale = spriteRendererContainer.transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (flipX ? -1 : 1);
        spriteRendererContainer.transform.localScale = scale;
    }

    void OnDirectionChanged(FacingDirection oldDir, FacingDirection newDir)
    {
        PlayDirectionalAnimation("Idle");
    }

    public enum FacingDirection { Down, DownLeft, Left, UpLeft, Up, UpRight, Right, DownRight }

    public void UpdateFacingDirectionFromPosition()
    {
        currentFacingDirection = playerPosition switch
        {
            1 => FacingDirection.UpRight,
            2 => FacingDirection.Right,
            3 => FacingDirection.DownRight,
            4 => FacingDirection.DownLeft,
            5 => FacingDirection.Left,
            6 => FacingDirection.UpLeft,
            _ => FacingDirection.UpRight //Fallback
        };
    }

    public void PlayDirectionalAnimation(string baseAnim)
    {
        string animName = baseAnim + "_" + currentFacingDirection.ToString();

        Debug.Log($"[Animación] Ejecutando {animName}");

        // Luego si estamos en el servidor, hacemos un Rpc para todos los clientes
        if (isServer)
        {
            RpcPlayAnimation(animName);
            Debug.Log("Estoy reproduciendo la animación sin el error, la csm esto es un caos");
        }
        else
        {
            animator.Play(animName); // Reproducir localmente, para el actionEvent de las animaciones
        }
    }

    #endregion

    #region ShootAnimation

    private FacingDirection GetShootDirection(PlayerController target)
    {
        Vector3 myPos = transform.position;
        Vector3 targetPos = target.transform.position;

        Vector2 dir = (targetPos - myPos).normalized;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        // Normalizar ángulo entre 0 y 360
        if (angle < 0) angle += 360;

        if (angle >= 337.5 || angle < 22.5)
            return FacingDirection.Right;
        else if (angle >= 22.5 && angle < 67.5)
            return FacingDirection.UpRight;
        else if (angle >= 67.5 && angle < 112.5)
            return FacingDirection.Up;
        else if (angle >= 112.5 && angle < 157.5)
            return FacingDirection.UpLeft;
        else if (angle >= 157.5 && angle < 202.5)
            return FacingDirection.Left;
        else if (angle >= 202.5 && angle < 247.5)
            return FacingDirection.DownLeft;
        else if (angle >= 247.5 && angle < 292.5)
            return FacingDirection.Down;
        else
            return FacingDirection.DownRight;
    }


    #endregion

    #region CLIENT
    private void OnShootButton()
    {
        if (!isOwned) return;
        if (isAiming) return;

        isAiming = true;
        selectedAction = ActionType.Shoot;

        if (crosshairPrefab && crosshairInstance == null)
        {
            crosshairInstance = Instantiate(crosshairPrefab);
            Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePosition.z = 0;
            crosshairInstance.transform.position = mousePosition; // <- ¡esto evita que se vea en el centro!
        }

        HighlightButton(shootButton);//Resaltar botón
    }

    private void OnSuperShootButton()
    {
        if (!isLocalPlayer && !isOwned) return;
        if (isAiming) return;

        isAiming = true;
        selectedAction = ActionType.SuperShoot;

        if (crosshairPrefab && crosshairInstance == null)
        {
            crosshairInstance = Instantiate(crosshairPrefab);
            Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePosition.z = 0;
            crosshairInstance.transform.position = mousePosition; // <- ¡esto evita que se vea en el centro!
        }

        HighlightButton(superShootButton);
    }

    private void OnReloadButton()
    {
        if (!isLocalPlayer && !isOwned) return;

        CmdRegisterAction(ActionType.Reload, null);
        HighlightButton(reloadButton);//Resaltar botón
    }

    private void OnCoverButton()
    {
        if (!isLocalPlayer && !isOwned) return;

        CmdRegisterAction(ActionType.Cover, null);
        HighlightButton(coverButton);//Resaltar botón
    }

    #region CuentaRegresivaFase

    [ClientRpc]
    public void RpcShowCountdown(float executionTime)
    {
        StartCoroutine(ShowCountdownUI(executionTime));
    }

    private IEnumerator ShowCountdownUI(float executionTime)
    {
        countdownText.gameObject.SetActive(true); // Mostrar el texto en pantalla

        float waitTime = executionTime - 3f; // Esperar hasta los últimos 3 segundos
        yield return new WaitForSeconds(waitTime);

        // Últimos 3 segundos
        countdownText.text = "PREPARADOS\n3";
        yield return new WaitForSeconds(1f);

        countdownText.text = "LISTOS\n2";
        yield return new WaitForSeconds(1f);

        countdownText.text = "YA!\n1";
        yield return new WaitForSeconds(1f);

        countdownText.text = "";

        countdownText.gameObject.SetActive(false); // Ocultar el texto después de la cuenta regresiva
    }

    #endregion

    #region HighlightButton
    private void HighlightButton(Button button)
    {
        if (selectedButton != null) //Si hay un botón seleccionado previamente, restablecer su color
        {
            ColorBlock colors = selectedButton.colors;
            colors.normalColor = defaultButtonColor;
            selectedButton.colors = colors;
        }

        if (button != null)// Resaltar nuevo botón
        {
            ColorBlock newColors = button.colors;
            newColors.normalColor = highlightedColor;
            newColors.selectedColor = highlightedColor; //Mantener nuevo color
            button.colors = newColors;

            selectedButton = button;// Guerdar el botón seleccionado
        }
    }

    [ClientRpc]
    public void RpcResetButtonHightLight()
    {
        if (!isLocalPlayer && !isOwned) return;

        if (selectedButton != null)
        {
            ColorBlock colors = selectedButton.colors;
            colors.normalColor = defaultButtonColor;
            colors.selectedColor = defaultButtonColor;
            selectedButton.colors = colors;

            selectedButton = null;
        }
    }

    #endregion

    [ClientRpc]
    public void RpcSetTargetIndicator(PlayerController shooter, PlayerController target)
    {
        if (!isOwned || !isAlive || this != shooter) return; //Solo ejecuta en el cliente que disparó

        //Desactivamos cualquier indicador previo
        PlayerController[] allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (var player in allPlayers)
        {
            if (player.targetIndicator != null)
                player.targetIndicator.SetActive(false);
        }

        //Activamos el indicador del enemigo seleccionado
        if (target != null && target.targetIndicator != null)
        {
            target.targetIndicator.SetActive(true);
        }
    }

    //Mostrar pantalla de derrota (solo en el cliente del jugador muerto)
    
    [ClientRpc]
    public void RpcUpdateCover(bool coverState)
    {
        isCovering = coverState;
        Debug.Log($"[CLIENT] {playerName} -> isCovering actualizado a {isCovering}");
    }

    #region UI Handling

    [ClientRpc]
    public void RpcCancelAiming()
    {
        if (!isLocalPlayer && !isOwned) return;

        if (crosshairInstance != null)
        {
            Destroy(crosshairInstance);
            crosshairInstance = null;
        }

        isAiming = false;
        selectedAction = ActionType.None;
    }

    [ClientRpc]
    public void RpcOnDeath()
    {
        if (isLocalPlayer || isOwned)
        {
            deathCanvas.SetActive(true);
        }

        PlayDirectionalAnimation("Death"); //Animacion de muerte 
        coverProbabilityText.gameObject.SetActive(false);
        healthText.gameObject.SetActive(false);
        ammoText.gameObject.SetActive(false);
    }

    [ClientRpc]
    public void RpcOnDeathOrDraw()
    {
        if (isLocalPlayer || isOwned)
        {
            drawCanvas.SetActive(true);
        }

        PlayDirectionalAnimation("Death"); //Animacion de muerte 
        coverProbabilityText.gameObject.SetActive(false);
        healthText.gameObject.SetActive(false);
        ammoText.gameObject.SetActive(false);
    }

    [ClientRpc]
    public void RpcOnVictory()
    {
        if (isLocalPlayer || isOwned)
        {
            victoryCanvas.SetActive(true);
        }
    }

    #endregion

    [Command]
    public void CmdRegisterAction(ActionType actionType, PlayerController target)
    {
        if (GameManager.Instance == null) Debug.Log("No tienesGameManager pero registrasteaccion en server");
        selectedAction = actionType;//Enviar accion seleccionada al servidor

        GameManager.RegisterAction(this, actionType, target);

        RpcSetTargetIndicator(this, target); //Indicador del cliente que disparó

    }

    #endregion

    #region SERVER

    [Server]
    public void ServerAttemptShoot(PlayerController target)
    {
        if (ammo <= 0 || target == null) return;

        ammo--;
        bulletsFired++; //Sumar el contador de balas disparadas
        GameStatistic stat = FindFirstObjectByType<GameStatistic>(); if (stat != null && isServer) stat.UpdatePlayerStats(this); // Actualizar en el GameStatistics

        RpcPlayShootEffect(target.transform.position);

        int damage = 1;

        if (hasDoubleDamage)
        {
            damage = 2;
            hasDoubleDamage = false;
            Debug.Log($"{playerName} tiene DAÑO DOBLE activo.");
        }

        if (selectedAction == ActionType.Shoot)
        {
            // Verificamos si Balas Oxidadas está activo y si el disparo falla (25% de probabilidad)
            if (rustyBulletsActive && Random.value < 0.25f)
            {
                Debug.Log($"{playerName} disparó, pero la bala falló debido a Balas Oxidadas.");
                RpcPlayAnimation("ShootFail");
                return; // Bala se gasta, pero no hace daño
            }

            FacingDirection shootDir = GetShootDirection(target);
            RpcPlayAnimation("Shoot_" + shootDir.ToString());
        }

        if (selectedAction == ActionType.SuperShoot)
        {
            ammo++;// sumamos una bala para compensar la que perdimos antes
            ammo -= minBulletSS;// Restamos las balas especiales

            // Verificamos si Balas Oxidadas está activo y si el disparo falla (25% de probabilidad)
            if (rustyBulletsActive && Random.value < 0.25f)
            {
                Debug.Log($"{playerName} disparó, pero la bala falló debido a Balas Oxidadas.");
                RpcPlayAnimation("ShootFail");
                return; // Bala se gasta, pero no hace daño
            }

            if (target.isCovering)
            {
                target.isCovering = false; //Desactiva la cobertura del objetivo en el servidor
                target.RpcUpdateCover(false);// Sincroniza la cobertura con los demás clientes
                target.RpcPlayAnimation("CoverBroken");
                Debug.Log($"{playerName} usó SUPERSHOOT y forzó a {target.playerName} a salir de cobertura");
            }

            FacingDirection shootDir = GetShootDirection(target);
            RpcPlayAnimation("SuperShoot_" + shootDir.ToString());
        }

        if (target.isCovering)
        {
            Debug.Log($"[SERVER] {target.playerName} está cubierto. Disparo bloqueado..");

            target.wasShotBlockedThisRound = true;
            return;
        }

        if (!target.isAlive)//Si el jugador estaba muerto antes de dispararle
        {
            Debug.Log($"{playerName} le disparó al cadaver de {target.playerName}.");
            return;
        }

        if (!canDealDamageThisRound)
        {
            Debug.Log($"[Talisman] {playerName} disparó a {target.playerName}, pero perdió prioridad. No se causa daño.");
            return;
        }

        damageDealt += damage; // Sumar daño hecho
        sucessfulShots++; // Sumar 1 a disparo exitoso
        target.TakeDamage(damage, this);
        hasDamagedAnotherPlayerThisRound = true;
        
        lastShotTarget = target; // Almacenar víctima de disparo
        Debug.Log($"{playerName} disparó a {target.playerName}. Balas restantes: {ammo}");
    }

    [Server]
    public void ServerReload()
    {
        PlayDirectionalAnimation("Reload");
        if (isDarkReloadEnabled)
        {
            ammo++;
            ammo++;
            Debug.Log($"{playerName} recargó 2 balas debido a Carga Oscura.");
            bulletsReloaded += 2;

            GameStatistic stat = FindFirstObjectByType<GameStatistic>(); if (stat != null && isServer) stat.UpdatePlayerStats(this); // Actualizar en el GameStatistics
        }
        else
        {
            ammo++;
            bulletsReloaded++;

            GameStatistic stat = FindFirstObjectByType<GameStatistic>(); if (stat != null && isServer) stat.UpdatePlayerStats(this); // Actualizar en el GameStatistics
        }
    }

    [Server]
    public void TakeDamage(int damageAmount, PlayerController attacker)
    {
        if (!isAlive || isCovering ) return;

        if (!GameManager.AllowAccumulatedDamage() && GameManager.HasTakenDamage(this)) //Verificar si el daño se debe acumular o no
        {
            Debug.Log($"{playerName} ya recibió daño en esta ronda, ignorando el ataque.");
            return;
        }

        // Marcar que el jugador ha recibido daño en esta ronda si el daño no se acumula
        if (!GameManager.AllowAccumulatedDamage())
        {
            GameManager.RegisterDamagedPlayer(this);
        }

        PlayDirectionalAnimation("Stunned");

        health -= damageAmount;

        if (health <= 0)
        {
            if (!isAlive) return; // Segunda verificación para evitar doble ejecución
            isAlive = false;

            Debug.Log($"{playerName} ha sido eliminado.");

            // Detectar quién fue el asesino usando el mismo criterio que GameManager ya usa
            PlayerController killer = attacker;

            if (killer != null && killer != this)
            {
                killer.kills++;
                RolesManager rolesManager = FindRolesManagerInScene();

                if (rolesManager != null)
                {
                    rolesManager.RegisterKill(killer, this);
                }

                Debug.Log($"[Kills] {killer.playerName} mató a {playerName}, es un HOMICIDA, un SIKOPATA, un ASESINO, llamen a la POLIZIA por el AMOR DE DIOS.");

                var stat = FindFirstObjectByType<GameStatistic>();
                if (stat != null && isServer)
                {
                    stat.UpdatePlayerStats(killer);
                }
            }

            GameManager.PlayerDied(this);
        }
    }
    private RolesManager FindRolesManagerInScene()
    {
        Scene myScene = gameObject.scene;

        foreach (var rootObj in myScene.GetRootGameObjects())
        {
            RolesManager manager = rootObj.GetComponentInChildren<RolesManager>();
            if (manager != null)
                return manager;
        }

        Debug.LogWarning("[PlayerController] No se encontró RolesManager en esta escena.");
        return null;
    }


    [Server]
    public void ServerHeal(int amount)
    {
        health = Mathf.Min(health + amount, fullHealth);
        Debug.Log($"{playerName} se ha curado {amount} de vida.");
    }

    [Server]
    public void ServerHealFull()
    {
        health = fullHealth; // Curación total
        Debug.Log($"{playerName} se ha curado completamente.");
    }

    #endregion

    #region OnStopClient

    public override void OnStopServer()
    {
        base.OnStopServer();

        /*if (GameManager.Instance != null)
        {*/
            GameManager./*Instance.*/PlayerDisconnected(this);
        /*}*/

        GameStatistic stat = FindFirstObjectByType<GameStatistic>();

        if (stat != null && isServer)
        {
            stat.UpdatePlayerStats(this, true);
        }
    }

    #endregion

    #region GameMode Roulette

    [TargetRpc]
    public void TargetStartRouletteWithWinner(NetworkConnection target, float duration, int winnerIndex)
    {
        if (!isOwned) return;

        if (gameModifierCanvas != null)
            gameModifierCanvas.SetActive(true);

        if (roulette != null)
        {
            // Nuevo: conversión index -> GameModifierType
            GameModifierType type = (GameModifierType)winnerIndex;
            roulette.StartRoulette(type, duration);
        }
    }

    #endregion

    [ClientRpc]
    public void RpcHideLoadingScreen()
    {
        if (isOwned)
        {
            Debug.Log("Clienteeeeeeeeeeeeeeeeeeeee");
        }

        var screen = FindFirstObjectByType<LoadingScreenManager>();
        if (screen != null)
        {
            screen.HideLoading();
            Debug.Log("[CLIENT] Canvas de carga ocultado por GameManager");
        }
        else
        {
            Debug.LogWarning("[CLIENT] No se encontró LoadingScreenManager para ocultar");
        }
    }
}