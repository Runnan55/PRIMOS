using Mirror;
using Mirror.Examples.Common.Controllers.Tank;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    [SerializeField] TextMesh healthText;
    [SerializeField] Renderer renderMaterial;

    [SyncVar (hook = nameof(WhenHealthChangues))]
    [SerializeField] private int Health;

    [SyncVar (hook = nameof(WhenColorChangues))]
    [SerializeField] private Color color;

    [SerializeField] private Rigidbody rb;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform spawnTransform;

    #region Server
    [Server]
    public void SetHealthPlayer (int Health)
    {
         this.Health = Health;
    }

    [Command]

    public void CmdSetHealthPlayer(int Health)
    {
        SetHealthPlayer(Health);
    }

    [Server]
    public void SetColorPlayer(Color color)
    {
        this.color = color;
    }
    #endregion

    #region Cliente
    public void WhenHealthChangues(int OldHealth, int NewHealth)
    {
        healthText.text = NewHealth.ToString();
    }

    public void WhenColorChangues(Color oldColor, Color newColor)
    {
        renderMaterial.material.SetColor("_BaseColor", newColor);
    }

    [ContextMenu("Bajar Vida")]
    public void SetMyhealth()
    {
        CmdSetHealthPlayer(Health - 1);
    }

    private void Update()
    {
        if (isLocalPlayer)
        {
            if (Input.GetMouseButtonDown(0))
            {
                CmdFire();
            }
        }
    }
    private void FixedUpdate()
    {
        if (isLocalPlayer)
        {
            Vector3 inputAxis = new Vector3(Input. GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
            rb.linearVelocity = inputAxis * 500 * Time.fixedDeltaTime;

            Rotate();
        }
    }

    [Command]
    void CmdFire()
    {
        GameObject projectile = Instantiate(projectilePrefab, spawnTransform.position, spawnTransform.rotation);
        NetworkServer.Spawn(projectile);
    }

    void Rotate()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if ( Physics.Raycast(ray, out hit, 100))
        {
            Debug.DrawLine(ray.origin, hit.point);
            Vector3 lookRotation = new Vector3(hit.point.x, transform.position.y, hit.point.z);
             transform.LookAt(lookRotation);
        }
    }

    [ServerCallback]

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Bullet"))
        {
            --Health;
            if( Health == 0) NetworkServer.Destroy(gameObject);
        }
    }

    #endregion
}
