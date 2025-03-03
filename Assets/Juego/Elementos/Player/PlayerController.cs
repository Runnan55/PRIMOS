using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum ActionType
{
    None, //Opción por si el jugador no elige nada
    Shoot,
    Reload,
    Cover
}

public class PlayerAction
{
    public ActionType type;
    public PlayerController target; //Se usa solo para el disparo

    public PlayerAction(ActionType type,PlayerController target = null)
    {
        this.type = type;
        this.target = target;
    }
}

public class PlayerController : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnAliveChanged))] public bool isAlive = true;
    [SyncVar(hook = nameof(OnAmmoChanged))] public int ammo = 3;
    [SyncVar(hook = nameof(OnHealthChanged))] public int health = 3;
    [SyncVar] public bool isCovering = false;
    
    private static PlayerController targetEnemy; //Enemigo seleccionado
    private static bool isAiming = false; //Indica si se está apuntando

    public ActionType selectedAction = ActionType.None;

    [Header("UI Elements")]
    public GameObject playerCanvas;
    public GameObject deathCanvas;
    public GameObject victoryCanvas;
    public Button shootButton;
    public Button reloadButton;
    public Button coverButton;
    public TMP_Text healthText;
    public TMP_Text ammoText;

    [Header("Crosshair")]
    public GameObject crosshairPrefab; //Prefab de la mirilla
    private GameObject crosshairInstance; //Instancia que crea el script cuando seleccionamos disparar


    private void Start()
    {
        if (isLocalPlayer)
        {
            deathCanvas.SetActive(false);
            victoryCanvas.SetActive(false);

            if (playerCanvas)
                playerCanvas.SetActive(true);
            

            if (shootButton) shootButton.onClick.AddListener(() => OnShootButton());
            if (reloadButton) reloadButton.onClick.AddListener(() => CmdRegisterAction(ActionType.Reload, null));
            if (coverButton) coverButton.onClick.AddListener(() => CmdRegisterAction(ActionType.Cover, null));

            UpdateUI();

            //Esto es un apaño temporal para saber quien es tu jugador
            SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.color = new Color(0.1f, 0.1f, 0.5f, 1f); // Azul oscuro (RGBA)
            }

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
        OnHealthChanged(health, health);
        OnAmmoChanged(ammo, ammo);
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
                    Debug.Log($"Objetivo seleccionado: {clickedPlayer.gameObject.name}");
                    CmdRegisterAction(ActionType.Shoot, clickedPlayer);
                    Destroy(crosshairInstance);
                    isAiming = false;
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

        if (shootButton)
            shootButton.interactable = newAmmo > 0;
    }

    private void UpdateUI()
    {
        if (healthText)
            healthText.text = $"Vida: {health}";

        if (ammoText)
            ammoText.text = $"Balas: {ammo}";
    }

    #endregion

    #region CLIENT
    private void OnShootButton()
    {
        if (!isLocalPlayer) return;
        if (isAiming) return;

        isAiming = true;
        Debug.Log("Modo de apuntado activado");

        if(crosshairPrefab && crosshairInstance == null)
        {
            crosshairInstance = Instantiate(crosshairPrefab);
        }
    }

    private void OnCoverButton()
    {
        if(!isLocalPlayer) return;
        CmdRegisterAction(ActionType.Cover, null);
    }
    // 🔹 Mostrar pantalla de derrota (solo en el cliente del jugador muerto)
    [ClientRpc]
    public void RpcShowDefeat()
    {
        if (isLocalPlayer && deathCanvas != null)
        {
            Debug.Log("Mostrando pantalla de derrota.");
            deathCanvas.SetActive(true);
        }
    }

    // 🔹 Mostrar pantalla de victoria (solo en el cliente del ganador)
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
        Debug.Log($"[CLIENT] Recibido RpcUpdateCover({coverState}) en {gameObject.name}");
        isCovering = coverState;
        Debug.Log($"[CLIENT] {gameObject.name} -> isCovering actualizado a {isCovering}");
    }

    #region UI Handling

    [ClientRpc]
    void RpcOnDeath()
    {
        if (isLocalPlayer)
        {
            deathCanvas.SetActive(true);
            DisableButtons();
        }
        
    }

    [ClientRpc]
    public void RpcOnVictory()
    {
        if (isLocalPlayer)
        {
            victoryCanvas.SetActive(true);
            DisableButtons();
        }
    }/*
    public void ShowVictoryUI()
    {
        if (victoryCanvas != null)
        {
            victoryCanvas.SetActive(true);
        }
        DisableButtons();
    }

    public void ShowDefeatUI()
    {
        if (deathCanvas != null)
        {
            deathCanvas.SetActive(true);
        }
        DisableButtons();
    }*/

    private void DisableButtons()
    {
        shootButton.interactable = false;
        reloadButton.interactable = false;
        coverButton.interactable = false;
    }

    #endregion
/*
    [Command]
    public void CmdNotifyVictory()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PlayerWon(this);
        }
    }
*/
    [Command]
    public void CmdRegisterAction(ActionType actionType, PlayerController target)
    {
        if (GameManager.Instance == null) return;

        GameManager.Instance.RegisterAction(this, actionType, target);
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

        if (target.isCovering)
        {
            Debug.Log($"[SERVER] {target.gameObject.name} está cubierto. Disparo bloqueado..");
            return;
        }

        target.TakeDamage();
        Debug.Log($"{gameObject.name} disparó a {target.gameObject.name}. Balas restantes: {ammo}");
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
        {
            Debug.Log($"{gameObject.name} bloqueó el daño porque está cubierto.");
            return; // No recibe daño
        }

        health--;
        Debug.Log($"{gameObject.name} recibió daño. Vida restante: {health}");

        if (health <= 0)
        {
            isAlive = false;
            Debug.Log($"{gameObject.name} ha sido eliminado.");

            RpcOnDeath(); // Notificar a todos los clientes que este jugador murió
            FindFirstObjectByType<GameManager>()?.PlayerDied(this);
            /*
                        //Enviar directamente al GameManager en el servidor
                        if (GameManager.Instance != null)
                        {
                            GameManager.Instance.ServerHandlePlayerDeath(this);
                        }*/
        }
    }

#endregion

}

