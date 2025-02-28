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
    [SyncVar] public bool isAlive = true;

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
            if(playerCanvas)
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

    private void OnShootButton()
    {
        if (!isLocalPlayer) return;

        isAiming = true;
        Debug.Log("Modo de apuntado activado");

        if(crosshairPrefab && crosshairInstance == null)
        {
            crosshairInstance = Instantiate(crosshairPrefab);
        }
    }

    private void Update()
    {
        if(!isLocalPlayer) return;

        //Mover mirilla con mouse
        if(isAiming && crosshairInstance)
        {
            Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePosition.z = 0;
            crosshairInstance.transform.position = mousePosition;
        }

        //Detectar clics para seleccionar enemigos o cancelar apuntado
        if(Input.GetMouseButtonDown(0))
        {
            RaycastHit2D hit = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero);

            if(hit.collider != null)
            {
                PlayerController clickedPlayer = hit.collider.GetComponent<PlayerController>();

                if(clickedPlayer != null && clickedPlayer != this)
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

    [ClientRpc]
    public void RpcCoveringOff()
    {
        isCovering = false;
    }

    [Command]
    public void CmdRegisterAction(ActionType actionType, PlayerController target)
    {
        if (GameManager.Instance == null) return;

        GameManager.Instance.RegisterAction(this, actionType, target);
    }

    [Command]
    public void CmdCover()
    {
        isCovering = true;
        Debug.Log($"{gameObject.name} se cubrió.");
    }

    [Server]
    public void ServerAttemptShoot(PlayerController target)
    {
        if (ammo <= 0 || target == null) return;

        ammo--;

        if (!target.isAlive)
        {
            Debug.Log($"{gameObject.name} intentó disparar a {target.gameObject.name}, pero ya estaba muerto.");
            return;
        }

        if (target.isCovering)
        {
            Debug.Log($"[SERVER] {target.gameObject.name} aún está cubierto. Disparo bloqueado..");
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

        health--;
        Debug.Log($"{gameObject.name} recibió daño. Vida restante: {health}");

        if (health <= 0)
        {
            isAlive = false;
            Debug.Log($"{gameObject.name} ha sido eliminado.");
            RpcHandleDeath();
        }
    }

    [ClientRpc]
    private void RpcUpdateCover(bool coverState)
    {
        isCovering = coverState;  //Se actualiza en todos los clientes
        Debug.Log($"[CLIENT] {gameObject.name} -> isCovering actualizado a {coverState}");
    }

    [ClientRpc]
    void RpcHandleDeath()
    {
        if (isLocalPlayer)
        {
            // Desactivar los botones
            shootButton.interactable = false;
            reloadButton.interactable = false;
            coverButton.interactable = false;

            // Mostrar mensaje de "Has muerto"
            if (deathCanvas)
                deathCanvas.SetActive(true);
        }
    }

    [ClientRpc]
    public void RpcDeclareVictory()
    {
        Debug.Log($"{gameObject.name} ha ganado la partida.");
    }
}

