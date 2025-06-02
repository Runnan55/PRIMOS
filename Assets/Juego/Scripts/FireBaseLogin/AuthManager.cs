using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using System.Runtime.InteropServices;
using Mirror;
using System;
using Unity.VisualScripting;
using SimpleJSON;

public class AuthManager : MonoBehaviour
{
    [Header("Firebase Web API Key")]
    [SerializeField] private string firebaseWebAPIKey = "AIzaSyBZ8_Wv37g_tFvIKvTDMcqnQTcEohLQSo";

    [Header("Panels")]
    public GameObject loginPanel;
    public GameObject registerPanel;

    [Header("Login UI")]
    public TMP_InputField emailInput_Login;
    public TMP_InputField passwordInput_Login;

    [Header("Register UI")]
    public TMP_InputField emailInput_Register;
    public TMP_InputField passwordInput_Register;
    public TMP_InputField passwordAgainInput_Register;

    [Header("Feedback")]
    public TMP_Text feedbackText;
    public TMP_Text passwordFeedbackText;

    private const string LoginUrl = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={0}";
    private const string SignUpUrl = "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={0}";

    public static AuthManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(this.gameObject);
    }

    private void Start()
    {
#if UNITY_WEBGL
        //CheckForSavedSession();
        Debug.Log("he comentado el checkforsavedsession porsilasmoscas sino no me permite abrir varias cuentas en el mismo navegador ");
#endif
    }

    public void ShowLoginPanel()
    {
        loginPanel.SetActive(true);
        registerPanel.SetActive(false);
        feedbackText.text = "";
    }

    public void ShowRegisterPanel()
    {
        loginPanel.SetActive(false);
        registerPanel.SetActive(true);
        passwordFeedbackText.text = "";
    }

    public void OnLoginButtonPressed()
    {
        string email = emailInput_Login.text.Trim();
        string password = passwordInput_Login.text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            feedbackText.text = "Please fill in all field.";
            return;
        }

        StartCoroutine(LoginUser(email, password));
    }

    public AllowedEmailManager allowedEmailManager; // Asignar en inspector o buscar con GetComponent

    public void OnRegisterButtonPressed()
    {
        string email = emailInput_Register.text.Trim();
        string password = passwordInput_Register.text;
        string passwordAgain = passwordAgainInput_Register.text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordAgain))
        {
            passwordFeedbackText.text = "Please fill in all field.";
            return;
        }

        if (password != passwordAgain)
        {
            passwordFeedbackText.text = "Password must be identical..";
            return;
        }

        // Validar email en lista permitida
        if (!allowedEmailManager.IsEmailAllowed(email))
        {
            passwordFeedbackText.text = "Email no autorizado para registro.";
            return;
        }

        if (!email.EndsWith("@gmail.com", System.StringComparison.OrdinalIgnoreCase))
        {
            passwordFeedbackText.text = "You should use a Gmail.com email address to register.";
        }

        if (password.Length < 7)
        {
            passwordFeedbackText.text = "Password should have at least 7 characters.";
        }

        passwordFeedbackText.text = ""; // Limpiar si está todo bien

        StartCoroutine(RegisterUser(email, password));
    }

    private IEnumerator LoginUser(string email, string password)
    {
        string url = string.Format(LoginUrl, firebaseWebAPIKey);

        string jsonPayload = JsonUtility.ToJson(new LoginRequest
        {
            email = email,
            password = password,
            returnSecureToken = true
        });

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

#if UNITY_2023_1_OR_NEWER
        if (request.result != UnityWebRequest.Result.Success)
#else
        if (request.isNetworkError || request.isHttpError)
#endif
        {
            //feedbackText.text = "Error de login: " + request.downloadHandler.text;

            string errorJson = request.downloadHandler.text;

            // Parsear usando SimpleJSON
            var parsedJson = JSON.Parse(errorJson);
            string errorMessage = parsedJson?["error"]?["message"];

            string message = "Unknown error during login.";

            if (!string.IsNullOrEmpty(errorMessage))
            {
                switch (errorMessage)
                {
                    case "EMAIL_NOT_FOUND":
                        message = "Cuenta o email no existente.";
                        break;
                    case "INVALID_PASSWORD":
                        message = "Password invalid.";
                        break;
                    case "USER_DISABLED":
                        message = "User disabled.";
                        break;
                    case "INVALID_EMAIL":
                        message = "Email format not valid.";
                        break;
                    case "INVALID_LOGIN_CREDENTIALS":
                        message = "Invalid login credentials.";
                        break;
                    case "TOO_MANY_ATTEMPTS_TRY_LATER":
                        message = "To many attempts, take a break.";
                        break;
                    default:
                        message = "Error: " + errorMessage;
                        break;
                }
            }
            else
            {
                message = errorJson;
            }

            feedbackText.text = message;

        }
        else
        {
            FirebaseLoginResponse loginResponse = JsonUtility.FromJson<FirebaseLoginResponse>(request.downloadHandler.text);

            WebGLStorage.SaveString("jwt_token", loginResponse.idToken);
            WebGLStorage.SaveString("refresh_token", loginResponse.refreshToken);
            WebGLStorage.SaveString("local_id", loginResponse.localId);
            WebGLStorage.SaveString("email", loginResponse.email);

            feedbackText.text = "Login exitoso. Bienvenido!";
            loginPanel.SetActive(false);
            registerPanel.SetActive(false);

            StartCoroutine(ConnectToMirrorServerAfterDelay());
        }
    }

    [Serializable]
    private class FirebaseErrorResponse
    {
        public FirebaseError error;
    }

    [Serializable]
    private class FirebaseError
    {
        public int code;
        public string message;
    }

    private IEnumerator ConnectToMirrorServerAfterDelay()
    {
        yield return new WaitForSeconds(1f);

        if (!NetworkClient.active)
        {
            NetworkManager.singleton.StartClient(); //Intenta conectarse como cliente
        }

        float timeout = 5f;
        float timer = 0f;

        //Esperar que se conecte
        while (!NetworkClient.isConnected && timer < timeout)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        if (!NetworkClient.isConnected)
        {
            Debug.Log($"[AuthManager] envio a jugador a servidor");
            OnConnectionFailed();
        }
    }

    private void OnConnectionFailed()
    {
        ShowLoginPanel();

        if (feedbackText != null)
        {
            feedbackText.text = "No se pudo conectar al servidor. Intenta nuevamente.";
        }
    }

    private IEnumerator RegisterUser(string email, string password)
    {
        string url = string.Format(SignUpUrl, firebaseWebAPIKey);

        string jsonPayload = JsonUtility.ToJson(new LoginRequest
        {
            email = email,
            password = password,
            returnSecureToken = true
        });

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

#if UNITY_2023_1_OR_NEWER
        if (request.result != UnityWebRequest.Result.Success)
#else
        if (request.isNetworkError || request.isHttpError)
#endif
        {
            feedbackText.text = "Error al registrar: " + request.downloadHandler.text;
        }
        else
        {
            feedbackText.text = "Registro exitoso. Ahora puedes iniciar sesión.";
            ShowLoginPanel();
        }
    }

    private void CheckForSavedSession()
    {
        string token = WebGLStorage.LoadString("jwt_token");

        if (!string.IsNullOrEmpty(token))
        {
            StartCoroutine(VerifySavedToken(token));
        }
        else
        {
            ShowLoginPanel();
        }
    }

    private IEnumerator VerifySavedToken(string idToken)
    {
        string url = "https://identitytoolkit.googleapis.com/v1/accounts:lookup?key=" + firebaseWebAPIKey;

        string jsonPayload = JsonUtility.ToJson(new TokenValidationRequest
        {
            idToken = idToken
        });

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

#if UNITY_2023_1_OR_NEWER
        if (request.result != UnityWebRequest.Result.Success)
#else
        if (request.isNetworkError || request.isHttpError)
#endif
        {
            string refreshToken = WebGLStorage.LoadString("refresh_token");
            if (!string.IsNullOrEmpty(refreshToken))
            {
                StartCoroutine(RefreshIdToken(refreshToken));
            }
            else
            {
                WebGLStorage.DeleteKey("jwt_token");
                ShowLoginPanel();
            }
        }
        else
        {
            loginPanel.SetActive(false);
            registerPanel.SetActive(false);
            feedbackText.text = "Sesión restaurada.";
        }
    }

    private IEnumerator RefreshIdToken(string refreshToken)
    {
        string url = "https://securetoken.googleapis.com/v1/token?key=" + firebaseWebAPIKey;

        WWWForm form = new WWWForm();
        form.AddField("grant_type", "refresh_token");
        form.AddField("refresh_token", refreshToken);

        UnityWebRequest request = UnityWebRequest.Post(url, form);
        request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");

        yield return request.SendWebRequest();

#if UNITY_2023_1_OR_NEWER
        if (request.result != UnityWebRequest.Result.Success)
#else
        if (request.isNetworkError || request.isHttpError)
#endif
        {
            WebGLStorage.DeleteKey("jwt_token");
            WebGLStorage.DeleteKey("refresh_token");
            ShowLoginPanel();
        }
        else
        {
            var response = JsonUtility.FromJson<TokenRefreshResponse>(request.downloadHandler.text);

            WebGLStorage.SaveString("jwt_token", response.id_token);
            WebGLStorage.SaveString("refresh_token", response.refresh_token);

            loginPanel.SetActive(false);
            registerPanel.SetActive(false);
            feedbackText.text = "Sesión restaurada.";
        }
    }

    private bool isLoggingOut = false;

    public void Logout()
    {
        if (isLoggingOut) return;
        isLoggingOut = true;

        WebGLStorage.DeleteKey("jwt_token");
        WebGLStorage.DeleteKey("refresh_token");
        StartCoroutine(DelayerShowLoginPanel());
        feedbackText.text = "Sesión cerrada.";
    }

    private IEnumerator DelayerShowLoginPanel()
    {
        yield return new WaitForSeconds(1f);
        ShowLoginPanel();
        isLoggingOut = false;
    }

    [System.Serializable]
    private class LoginRequest
    {
        public string email;
        public string password;
        public bool returnSecureToken;
    }

    [System.Serializable]
    private class FirebaseLoginResponse
    {
        public string idToken;
        public string refreshToken;
        public string localId;
        public string email;
    }

    [System.Serializable]
    private class TokenValidationRequest
    {
        public string idToken;
    }

    [System.Serializable]
    private class TokenRefreshResponse
    {
        public string id_token;
        public string refresh_token;
        public string user_id;
    }
}

internal class SerializableAttribute : Attribute
{
}