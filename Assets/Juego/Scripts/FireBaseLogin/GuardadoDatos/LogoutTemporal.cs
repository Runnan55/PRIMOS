using UnityEngine;

public class LogoutTemporal : MonoBehaviour
{
    public void OnLogoutButton()
    {
        AuthManager.Instance.Logout();
    }
}
