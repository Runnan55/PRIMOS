using System.Collections;
using Mirror;
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
    [SyncVar(hook = nameof(OnAliveChanged))] public bool isAlive = true;
    [SyncVar(hook = nameof(OnAmmoChanged))] public int ammo;
    [SyncVar(hook = nameof(OnHealthChanged))] public int health;
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
    public GameObject playerCanvas;
    public GameObject deathCanvas;
    public GameObject victoryCanvas;
    public GameObject targetIndicator; //Indicador visual de objetivo elegido
    public GameObject playerIndicator;
    public TMP_Text healthText;
    public TMP_Text ammoText;
    public TMP_Text coverProbabilityText;

    [Header("UI Buttons")]
    public Button shootButton;
    public Button reloadButton;
    public Button coverButton;
    public Button superShootButton;
    private Button selectedButton = null; //Último botón seleccionado
    [SerializeField] private Color defaultButtonColor = Color.white; //Color por defecto
    [SerializeField] private Color highlightedColor = Color.yellow; //Color resaltado

    [Header("Crosshair")]
    public GameObject crosshairPrefab; //Prefab de la mirilla
    private GameObject crosshairInstance; //Instancia que crea el script cuando seleccionamos disparar


    private void Start()
    {
        animator = GetComponent<Animator>();

        if (targetIndicator != null)
            targetIndicator.SetActive(false);

        if (playerIndicator != null)
            playerIndicator.SetActive(isLocalPlayer);

        if (isLocalPlayer)
        {
            deathCanvas.SetActive(false);
            victoryCanvas.SetActive(false);
            targetIndicator.SetActive(false);

            if (playerCanvas)
                playerCanvas.SetActive(true);

            if (shootButton) shootButton.onClick.AddListener(() => OnShootButton());
            if (reloadButton) reloadButton.onClick.AddListener(() => OnReloadButton());
            if (coverButton) coverButton.onClick.AddListener(() => OnCoverButton());
            if (superShootButton) superShootButton.onClick.AddListener(() => OnSuperShootButton());

            shootButton.interactable = ammo >= minBulletS;
            superShootButton.interactable = ammo >= minBulletSS;
        }
        else
        {
            if (playerCanvas)
                playerCanvas.SetActive(false);
        }
    }

    //Forzar sincronización inicial, sino no se verán los datos de los otros jugadores al inicio
    public override void OnStartClient()
    {
        base.OnStartClient();
        OnHealthChanged(health, health); //Forzamos mostrar vida al inicio aunque no cambie
        OnAmmoChanged(ammo, ammo); //Forzamos mostrar munición al inicio aunque no cambie
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
                        Debug.Log($"Objetivo seleccionado: {clickedPlayer.gameObject.name}");
                        CmdRegisterAction(ActionType.Shoot, clickedPlayer);
                    }
                    if (selectedAction == ActionType.SuperShoot)
                    {
                        Debug.Log($"Objetivo seleccionado: {clickedPlayer.gameObject.name}");
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

    private void OnAliveChanged(bool oldValue, bool newValue)
    {
        if (!newValue) //Sí estás muerto..
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.PlayerDied(this);
            }
        }
    }

    [ClientRpc]
    public void RpcUpdateCoverProbabilityUI(float updatedProbability)
    {
        if (coverProbabilityText != null)
        {
            coverProbabilityText.text = $"Cobertura\n{(updatedProbability * 100):0}%";
        }
    }

    // Hook para actualizar la UI de vida
    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        if (healthText)
            healthText.text = $"Vida: {newHealth}";
    }

    // Hook para actualizar la UI de balas
    private void OnAmmoChanged(int oldAmmo, int newAmmo)
    {
        if (ammoText)
            ammoText.text = $"Balas: {newAmmo}";
    }

    [TargetRpc]
    public void TargetUpdateUI(NetworkConnection target, int updatedAmmo)
    {
        if (healthText)
            healthText.text = $"Vida: {health}";

        if (ammoText)
            ammoText.text = $"Balas: {ammo}";
    }

    #endregion

    #region Animations

    [TargetRpc]
    public void TargetPlayButtonAnimation(NetworkConnection target, string animationTrigger, bool enableButtons)
    {
        // Solo habilitar los botones si enableButtons es true y cumplen con sus respectivas condiciones
        shootButton.interactable = enableButtons && ammo >= minBulletS;
        superShootButton.interactable = enableButtons && ammo >= minBulletSS;
        reloadButton.interactable = enableButtons;
        coverButton.interactable = enableButtons;

        Debug.Log($"[TARGETPLAYBUTTONANIMATION] Shoot Button: {shootButton.interactable}, SuperShoot Button: {superShootButton.interactable}");

        animator.SetTrigger(animationTrigger); // Ejecutar animación solo en el player local
    }

    #endregion

    #region CLIENT
    private void OnShootButton()
    {
        if (!isLocalPlayer) return;
        if (isAiming) return;

        isAiming = true;
        selectedAction = ActionType.Shoot;
        Debug.Log("Shoot activado");

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
        Debug.Log("SuperShoot activado");

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
        if (!isLocalPlayer || this != shooter) return; //Solo ejecuta en el cliente que disparó

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
            Debug.Log($"[CLIENT] Indicador de objetivo activado en {target.gameObject.name}");
        }
    }

    //Mostrar pantalla de derrota (solo en el cliente del jugador muerto)
    [ClientRpc]
    public void RpcShowDefeat()
    {
        if (isLocalPlayer && deathCanvas != null)
        {
            Debug.Log("Mostrando pantalla de derrota.");
            deathCanvas.SetActive(true);
        }
    }

    //Mostrar pantalla de victoria (solo en el cliente del ganador)
    [ClientRpc]
    public void RpcShowVictory()
    {
        if (isLocalPlayer && victoryCanvas != null)
        {
            Debug.Log("Mostrando pantalla de victoria.");
            victoryCanvas.SetActive(true);
        }
    }

    [ClientRpc]
    public void RpcUpdateCover(bool coverState)
    {
        isCovering = coverState;
        Debug.Log($"[CLIENT] {gameObject.name} -> isCovering actualizado a {isCovering}");
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
    void RpcOnDeath()
    {
        if (isLocalPlayer)
        {
            deathCanvas.SetActive(true);
            TargetPlayButtonAnimation(this.connectionToClient, "Irse", false);
        }

    }

    [ClientRpc]
    public void RpcOnVictory()
    {
        if (isLocalPlayer)
        {
            victoryCanvas.SetActive(true);
            TargetPlayButtonAnimation(this.connectionToClient, "Irse", false);
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
        #endregion

    #region SERVER

    [Server]
    public void ServerAttemptShoot(PlayerController target)
    {
        if (ammo <= 0 || target == null) return;

        ammo--;

        if (!target.isAlive)
        {
            Debug.Log($"{gameObject.name} le disparó al cadaver de {target.gameObject.name}.");
            return;
        }

        Debug.Log($"[SERVER] selectedAction actual: {selectedAction}");
        if (selectedAction == ActionType.SuperShoot)
        {
            ammo++;// sumamos una bala para compensar la que perdimos antes
            ammo -= minBulletSS;// Restamos las balas especiales
            if (target.isCovering)
            {
                target.isCovering = false; //Desactiva la cobertura del objetivo en el servidor
                target.RpcUpdateCover(false);// Sincroniza la cobertura con los demás clientes
                target.RpcPlayAnimation("CoverBroken");
                Debug.Log($"{gameObject.name} usó SUPERSHOOT y forzó a {target.gameObject.name} a salir de cobertura");
            }
        }

        if (target.isCovering)
        {
            Debug.Log($"[SERVER] {target.gameObject.name} está cubierto. Disparo bloqueado..");
            return;
        }

        target.TakeDamage();
        Debug.Log($"{gameObject.name} disparó a {target.gameObject.name}. Balas restantes: {ammo}");
    }

    [ClientRpc]
    public void RpcPlayAnimation(string animation)
    {
        GetComponent<NetworkAnimator>().animator.Play(animation);//Esto sirve por ejemplo para que el player llame animación en otro player, tambien se puede llamar desde el Server
    }

    [Server]
    public void ServerReload()
    {
        ammo++;
        Debug.Log($"{gameObject.name} recargó. Balas actuales: {ammo}");
    }

    [Server]
    public void TakeDamage()
    {
        if (!isAlive) return;

        if (isCovering)
        if (isCovering)
        {
            Debug.Log($"{gameObject.name} bloqueó el daño porque está cubierto.");
            return; // No recibe daño
        }

        GetComponent<NetworkAnimator>().animator.Play("ReceiveDamage");

        health--;
        Debug.Log($"{gameObject.name} recibió daño. Vida restante: {health}");

        if (health <= 0)
        {
            isAlive = false;
            Debug.Log($"{gameObject.name} ha sido eliminado.");

            RpcOnDeath(); // Notificar a todos los clientes que este jugador murió
            FindFirstObjectByType<GameManager>()?.PlayerDied(this);
        }
    }

    #endregion

}