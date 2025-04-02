using System.Collections;
using Mirror;
using NUnit.Framework.Constraints;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    [SyncVar] public bool isCovering = false;
    [SerializeField] private int minBulletS = 1;
    [SerializeField] private int minBulletSS = 3;
    [SerializeField] public float[] coverProbabilities = { 1f, 0.5f, 0.01f };//Probabilidades 100%, 50%, 1%
    [SyncVar] public int consecutiveCovers = 0;

    private static PlayerController targetEnemy; //Enemigo seleccionado
    private static bool isAiming = false; //Indica si se está apuntando

    private Animator animator;

    public ActionType selectedAction = ActionType.None;

    [Header("UI Elements")]
    public GameObject canvasInicioPartida; //Elegir nombre jugador
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
    public TMP_InputField nameInputField;

    [Header("UI Buttons")]
    public Button shootButton;
    public Button reloadButton;
    public Button coverButton;
    public Button superShootButton;
    private Button selectedButton = null; //Último botón seleccionado
    [SerializeField] private Color defaultButtonColor = Color.white; //Color por defecto
    [SerializeField] private Color highlightedColor = Color.yellow; //Color resaltado

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

    [Header("ShootBulletAnimation")]
    public GameObject projectilePrefab;
    public Transform shootOrigin;

    [Header("GameStatistics")]
    [SyncVar] public int bulletsReloaded = 0;
    [SyncVar] public int bulletsFired = 0;
    [SyncVar] public int damageDealt = 0;
    [SyncVar] public int timesCovered = 0;

    private void Start()
    {
        if (isLocalPlayer)
        {
            shootButton.interactable = (false);
            superShootButton.interactable = (false);
            reloadButton.interactable = (false);
            coverButton.interactable = (false);

            canvasInicioPartida.SetActive(true); // Solo el jugador local ve su propio Canvas
            localPlayerIndicator.SetActive(true);

            nameInputField.gameObject.SetActive(true);
            nameInputField.characterLimit = 20; //Límite de caracteres en InputField
            nameInputField.onEndEdit.AddListener(CmdSetPlayerName);

            deathCanvas.SetActive(false);
            victoryCanvas.SetActive(false);
            drawCanvas.SetActive(false);
            targetIndicator.SetActive(false);
            playerCanvas.SetActive(true);

            if (shootButton) shootButton.onClick.AddListener(() => OnShootButton());
            if (reloadButton) reloadButton.onClick.AddListener(() => OnReloadButton());
            if (coverButton) coverButton.onClick.AddListener(() => OnCoverButton());
            if (superShootButton) superShootButton.onClick.AddListener(() => OnSuperShootButton());
        }
        else
        {
            canvasInicioPartida.SetActive(false); // Ocultar para los demás jugadores
            playerCanvas.SetActive(false);
            localPlayerIndicator.SetActive(false);

            nameInputField.gameObject.SetActive(false);
        }

        fullHealth = health; //guardamos el valor inicial de health

        animator = GetComponent<Animator>();

        if (targetIndicator != null)
            targetIndicator.SetActive(false);
    }

    //Forzar sincronización inicial, sino no se verán los datos de los otros jugadores al inicio
    public override void OnStartClient()
    {
        base.OnStartClient();
        OnHealthChanged(health, health); //Forzamos mostrar vida al inicio aunque no cambie
        OnAmmoChanged(ammo, ammo); //Forzamos mostrar munición al inicio aunque no cambie
        //No forzamos un OnNameChanged porque el nombre se actualiza al inicio de la partida
    }

    public override void OnStartServer()
    {
        FindFirstObjectByType<GameManager>()?.RegisterPlayer(this);
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

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
                }
                else
                {
                    Debug.Log("Clic en objeto no válido, Apuntado cancelado");
                    Destroy(crosshairInstance);
                    isAiming = false;
                }
            }
            else
            {
                Debug.Log("Clic en ningún objeto, cancelado por chistoso ;)");
                Destroy(crosshairInstance);
                isAiming = false;
            }
        }
    }

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
        if (isLocalPlayer && newValue)
        {
            canvasInicioPartida.SetActive(false);
        }
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
            healthText.text = $"Vida: {newHealth}";
    }

    private void OnAmmoChanged(int oldAmmo, int newAmmo)
    {
        if (ammoText)
            ammoText.text = $"Balas: {newAmmo}";
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

    #region Animations

    [TargetRpc]
    public void TargetPlayButtonAnimation(NetworkConnection target, string animationTrigger, bool enableButtons)
    {
        if (!isAlive || !gameObject.activeInHierarchy) return;// Evita ejecutar si el jugador está muerto o desactivado

        // Solo habilitar los botones si enableButtons es true y cumplen con sus respectivas condiciones
        shootButton.interactable = enableButtons && ammo >= minBulletS;
        superShootButton.interactable = enableButtons && ammo >= minBulletSS;
        reloadButton.interactable = enableButtons;

        coverButton.interactable = enableButtons && !isVeryHealthy; //Requiere también que el jugador no tenga el bool de Caceria de Lider activo

        animator.SetTrigger(animationTrigger); // Ejecutar animación solo en el player local
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

    #region CLIENT
    private void OnShootButton()
    {
        if (!isLocalPlayer) return;
        if (isAiming) return;

        isAiming = true;
        selectedAction = ActionType.Shoot;

        if (crosshairPrefab && crosshairInstance == null)
        {
            crosshairInstance = Instantiate(crosshairPrefab);
        }

        HighlightButton(shootButton);//Resaltar botón
    }

    private void OnSuperShootButton()
    {
        if (!isLocalPlayer) return;
        if (isAiming) return;

        isAiming = true;
        selectedAction = ActionType.SuperShoot;

        if (crosshairPrefab && crosshairInstance == null)
        {
            crosshairInstance = Instantiate(crosshairPrefab);
        }

        HighlightButton(superShootButton);
    }

    private void OnReloadButton()
    {
        if (!isLocalPlayer) return;
        CmdRegisterAction(ActionType.Reload, null);

        HighlightButton(reloadButton);//Resaltar botón
    }

    private void OnCoverButton()
    {
        if (!isLocalPlayer) return;
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

        countdownText.text = "¡YA!\n1";
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
        if (!isLocalPlayer) return;

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
        if (!isLocalPlayer || !isAlive || this != shooter) return; //Solo ejecuta en el cliente que disparó

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
        if (!isLocalPlayer) return;

        if (crosshairInstance != null)
        {
            Destroy(crosshairInstance);
            crosshairInstance = null;
        }

        isAiming = false;
    }

    [ClientRpc]
    public void RpcOnDeath()
    {
        if (isLocalPlayer)
        {
            deathCanvas.SetActive(true);
        }

        GetComponent<NetworkAnimator>().animator.Play("Death");//Animacion de muerte 
        coverProbabilityText.gameObject.SetActive(false);
        healthText.gameObject.SetActive(false);
        ammoText.gameObject.SetActive(false);
    }

    [ClientRpc]
    public void RpcOnDeathOrDraw()
    {
        if (isLocalPlayer)
        {
            drawCanvas.SetActive(true);
        }

        GetComponent<NetworkAnimator>().animator.Play("Death");//Animacion de muerte 
        coverProbabilityText.gameObject.SetActive(false);
        healthText.gameObject.SetActive(false);
        ammoText.gameObject.SetActive(false);
    }
/*
    private IEnumerator PlayDeathAnimationWithDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        GetComponent<NetworkAnimator>().animator.Play("Death");//Animacion de muerte 
        coverProbabilityText.gameObject.SetActive(false);
        healthText.gameObject.SetActive(false);
        ammoText.gameObject.SetActive(false);
    }*/

    [ClientRpc]
    public void RpcOnVictory()
    {
        if (isLocalPlayer)
        {
            victoryCanvas.SetActive(true);
        }
    }

    #endregion

    [Command]
    public void CmdRegisterAction(ActionType actionType, PlayerController target)
    {
        if (GameManager.Instance == null) return;

        selectedAction = actionType;//Enviar accion seleccionada al servidor

        GameManager.Instance.RegisterAction(this, actionType, target);

        RpcSetTargetIndicator(this, target); //Indicador del cliente que disparó

    }

    [Command]
    void CmdSetPlayerName(string newName)
    {
        if (!string.IsNullOrWhiteSpace(newName)) //Evitar nombres vacios de jugadores
        {
            playerName = newName;
            hasConfirmedName = true;
            GameManager.Instance.CheckAllPlayersReady(); //Llamar al servidor para iniciar partida
        }
    }

    #endregion

    #region SERVER

    [Server]
    public void ServerAttemptShoot(PlayerController target)
    {
        if (ammo <= 0 || target == null) return;

        ammo--;
        bulletsFired++; //Sumar el contador de balas disparadas

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
            RpcPlayAnimation("Shoot");

            // Verificamos si Balas Oxidadas está activo y si el disparo falla (25% de probabilidad)
            if (rustyBulletsActive && Random.value < 0.25f)
            {
                Debug.Log($"{playerName} disparó, pero la bala falló debido a Balas Oxidadas.");
                RpcPlayAnimation("ShootFail");
                return; // Bala se gasta, pero no hace daño
            }
        }

        if (selectedAction == ActionType.SuperShoot)
        {
            ammo++;// sumamos una bala para compensar la que perdimos antes
            ammo -= minBulletSS;// Restamos las balas especiales
            RpcPlayAnimation("SuperShoot");

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

        damageDealt += damage; // Sumar daño hecho

        target.TakeDamage(damage);
        lastShotTarget = target; // Almacenar víctima de disparo
        Debug.Log($"{playerName} disparó a {target.playerName}. Balas restantes: {ammo}");

        if (!target.isAlive)
        {
            RolesManager.Instance?.RegisterKill(this, target);
        }
    }

    [Server]
    public void ServerReload()
    {
        RpcPlayAnimation("Reload");
        if (isDarkReloadEnabled)
        {
            ammo++;
            ammo++;
            Debug.Log($"{playerName} recargó 2 balas debido a Carga Oscura.");
            bulletsReloaded += 2;
        }
        else
        {
            ammo++;
            bulletsReloaded++;
        }
        
    }

    [Server]
    public void TakeDamage(int damageAmount)
    {
        if (!isAlive || isCovering || GameManager.Instance == null) return;

        if (!GameManager.Instance.AllowAccumulatedDamage() && GameManager.Instance.HasTakenDamage(this)) //Verificar si el daño se debe acumular o no
        {
            Debug.Log($"{playerName} ya recibió daño en esta ronda, ignorando el ataque.");
            return;
        }

        // Marcar que el jugador ha recibido daño en esta ronda si el daño no se acumula
        if (!GameManager.Instance.AllowAccumulatedDamage())
        {
            GameManager.Instance.RegisterDamagedPlayer(this);
        }

        RpcPlayAnimation("ReceiveDamage");

        health -= damageAmount;

        if (health <= 0)
        {
            if (!isAlive) return; // Segunda verificación para evitar doble ejecución
            isAlive = false;

            Debug.Log($"{playerName} ha sido eliminado.");

            if (isServer && GameManager.Instance != null)// Esto solo se ejecuta en el servidor para evitar errores
            {
                GameManager.Instance.PlayerDied(this);
            }

        }
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

        if (GameManager.Instance != null)
        {
            GameManager.Instance.PlayerDisconnected(this);
        }
    }

    #endregion

    #region GameStatistics


    #endregion

}