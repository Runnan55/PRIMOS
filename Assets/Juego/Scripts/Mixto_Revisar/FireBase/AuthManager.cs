using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using System.Runtime.InteropServices;
using Mirror;
using System;
using SimpleJSON;
using UnityEngine.UI;

[Serializable]
public class JsLoginPayload
{
    public string uid;
    public string email;
    public string idToken;
    public string refreshToken;
}

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
    private bool isPasswordVisible_Login = false;

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

    private Coroutine refreshRoutine;
    private bool loginAccepted = false;

    public string GetCurrentUid() { return userId; }

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

    public void ToggleShowPassword_Login()
    {
        isPasswordVisible_Login = !isPasswordVisible_Login;
        passwordInput_Login.contentType = isPasswordVisible_Login ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
        passwordInput_Login.ForceLabelUpdate();
        // Aquí puedes cambiar el sprite del botón según el estado
    }

    private void Start()
    {
        TryAutoLoginOnce();
    }

    public void TryAutoLoginOnce()
    {

#if UNITY_WEBGL && !UNITY_EDITOR
    string savedToken   = WebGLStorage.LoadString("jwt_token");
    string savedRefresh = WebGLStorage.LoadString("refresh_token");
    string savedUid     = WebGLStorage.LoadString("local_id");
#else
        string savedToken = PlayerPrefs.GetString("firebase_idToken", "");
        string savedRefresh = PlayerPrefs.GetString("firebase_refreshToken", "");
        string savedUid = PlayerPrefs.GetString("firebase_userId", "");
#endif

        if (!string.IsNullOrEmpty(savedToken) &&
            !string.IsNullOrEmpty(savedRefresh) &&
            !string.IsNullOrEmpty(savedUid))
        {
            LogWithTime.Log("[AuthManager] Token encontrado, iniciando login silencioso...");
            idToken = savedToken;
            refreshToken = savedRefresh;
            userId = savedUid;

#if UNITY_SERVER
            FirebaseServerClient.SetServerCredentials(idToken, userId);
#endif

            if (refreshRoutine != null) StopCoroutine(refreshRoutine);
            refreshRoutine = StartCoroutine(RefreshTokenLoop());

            loginPanel.SetActive(false);
            registerPanel.SetActive(false);
            StartCoroutine(ConnectToMirrorServerAfterDelay());
        }
        else
        {
            LogWithTime.LogWarning("[AuthManager] No se encontró token guardado, se requeriría login manual.");
            //ShowLoginPanel();
            //De momento desactivamos lo de token manual
        }
    }

    #region Disconnect_Duplicate_User

    private bool loginHandlerRegistered;
    void OnEnable()
    {
        if (!loginHandlerRegistered)
        {
            NetworkClient.RegisterHandler<LoginResultMessage>(OnLoginResult);
            loginHandlerRegistered = true;
        }
    }

    private void OnLoginResult(LoginResultMessage msg)
    {
        if (msg.ok)
        {
            loginAccepted = true;

            // AHORA sí cerramos el login y dejamos ver tu MainMenu/UI
            if (loginPanel) loginPanel.SetActive(false);
            if (registerPanel) registerPanel.SetActive(false);
            if (feedbackText) feedbackText.text = "";
        }
        else
        {
            // Duplicado: mantenemos el login visible y mostramos error
            if (feedbackText) feedbackText.text = "Esta cuenta ya está conectada en otro dispositivo.";

            // desconecta el cliente si quedó conectado
            StartCoroutine(DisconnectClientIfConnected());
        }
    }

    private System.Collections.IEnumerator DisconnectClientIfConnected()
    {
        yield return null; // deja drenar el mensaje local
        if (NetworkClient.isConnected)
            NetworkManager.singleton.StopClient();
    }

    #endregion

    public void OnFirebaseLoginSuccess(string json)
    {
        var p = JsonUtility.FromJson<JsLoginPayload>(json);

        // Guardar persistente
        WebGLStorage.SaveString("jwt_token", p.idToken);
        WebGLStorage.SaveString("refresh_token", p.refreshToken);
        WebGLStorage.SaveString("local_id", p.uid);
        WebGLStorage.SaveString("email", p.email);

        // Actualizar estado interno
        idToken = p.idToken;
        refreshToken = p.refreshToken;
        userId = p.uid;

#if UNITY_SERVER
        FirebaseServerClient.SetServerCredentials(idToken, userId); // SOLO en build de servidor
#endif

        if (refreshRoutine != null) StopCoroutine(refreshRoutine);
        refreshRoutine = StartCoroutine(RefreshTokenLoop());

        // Continuar flujo normal (oculta UI y conecta)
        loginPanel.SetActive(false);
        registerPanel.SetActive(false);
        StartCoroutine(ConnectToMirrorServerAfterDelay());
    }

    public void OnFirebaseLoginError(string message)
    {
        LogWithTime.LogWarning("[AuthManager] JS auth error: " + message);
        //ShowLoginPanel();
        if (feedbackText != null) feedbackText.text = "Error de login: " + message;
    }


    #region Recover Password

    [System.Serializable]
    private class PasswordResetRequest
    {
        public string requestType = "PASSWORD_RESET";
        public string email;
    }

    public void OnForgotPasswordButtonPressed()
    {
        string email = emailInput_Login.text.Trim();

        if (string.IsNullOrEmpty(email))
        {
            feedbackText.text = "Please enter an email.";
            return;
        }

        StartCoroutine(SendPasswordResetEmail(email));
    }

    private IEnumerator SendPasswordResetEmail(string email)
    {
        string url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={firebaseWebAPIKey}";

        var payload = new PasswordResetRequest
        {
            email = email
        };

        string json = JsonUtility.ToJson(payload);

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
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
            var errorResponse = JSON.Parse(request.downloadHandler.text);
            string errorMessage = errorResponse?["error"]?["message"];

            if (errorMessage == "EMAIL_NOT_FOUND")
                feedbackText.text = "Correo inválido";
            else
                feedbackText.text = "Error: " + errorMessage;
        }
        else
        {
            feedbackText.text = "Email sent";
        }
    }

    #endregion

    public void ShowLoginPanel()
    {
        loginPanel.SetActive(true);
        registerPanel.SetActive(false);
        feedbackText.text = "";
    }

    public void ShowRegisterPanel()
    {
        AudioManager.Instance.PlaySFX("Clic");

        loginPanel.SetActive(false);
        registerPanel.SetActive(true);
        passwordFeedbackText.text = "";
    }

    public void OnLoginButtonPressed()
    {
        string email = emailInput_Login.text.Trim();
        string password = passwordInput_Login.text;

        AudioManager.Instance.PlaySFX("Clic");

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            feedbackText.text = "Please fill in all field.";
            return;
        }

        // Email está habilitado, continuar con login
        StartCoroutine(LoginUser(email, password));
    }

    public void OnRegisterButtonPressed()
    {
        string email = emailInput_Register.text.Trim();
        string password = passwordInput_Register.text;
        string passwordAgain = passwordAgainInput_Register.text;

        AudioManager.Instance.PlaySFX("Clic");

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

    public IEnumerator LoginUser(string email, string password)
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

            idToken = loginResponse.idToken;
            refreshToken = loginResponse.refreshToken;
            userId = loginResponse.localId;

#if UNITY_SERVER
            FirebaseServerClient.SetServerCredentials(idToken, userId);
#endif

            if (refreshRoutine != null) StopCoroutine(refreshRoutine);
            refreshRoutine = StartCoroutine(RefreshTokenLoop());

            loginPanel.SetActive(false);
            registerPanel.SetActive(false);

            StartCoroutine(ConnectToMirrorServerAfterDelay());
        }
    }

    // Parte 1: AuthManager.cs
    // Agregar al final de AuthManager.cs una sección separada para el login y refresh del servidor

    #region LoginHeadless y Refresh para Server

    private string idToken;
    private string refreshToken;
    private string userId;

#if UNITY_SERVER
    public IEnumerator LoginHeadlessForServer(string email, string password)
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

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorJson = request.downloadHandler.text;
            var parsedJson = JSON.Parse(errorJson);
            string errorMessage = parsedJson?["error"]?["message"];

            LogWithTime.LogError($"[AuthManager] LoginHeadless FALLÓ: {errorMessage ?? errorJson}");
        }
        else
        {
            var response = JsonUtility.FromJson<FirebaseLoginResponse>(request.downloadHandler.text);
            LogWithTime.Log("[AuthManager] LoginHeadless exitoso.");
            idToken = response.idToken;
            refreshToken = response.refreshToken;
            userId = response.localId;

            LogWithTime.Log("[AuthManager] UID recibido del servidor: " + response.localId);
            FirebaseServerClient.SetServerCredentials(response.idToken, response.localId);

            StartCoroutine(RefreshTokenLoop());
        }
}
#endif

    private IEnumerator RefreshTokenLoop()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(3300f); // ~55 min

            string url = "https://securetoken.googleapis.com/v1/token?key=" + firebaseWebAPIKey;

            WWWForm form = new WWWForm();
            form.AddField("grant_type", "refresh_token");
            form.AddField("refresh_token", refreshToken);

            UnityWebRequest request = UnityWebRequest.Post(url, form);
            request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JSON.Parse(request.downloadHandler.text);
                idToken = response["id_token"];
                refreshToken = response["refresh_token"];

#if UNITY_WEBGL && !UNITY_EDITOR
                WebGLStorage.SaveString("jwt_token", idToken);
                WebGLStorage.SaveString("refresh_token", refreshToken);
#else
                PlayerPrefs.SetString("firebase_idToken", idToken);
                PlayerPrefs.SetString("firebase_refreshToken", refreshToken);
#endif

#if UNITY_SERVER
                FirebaseServerClient.SetServerCredentials(idToken, userId);
#endif
                LogWithTime.Log("[AuthManager] Token refrescado correctamente.");
            }
            else
            {
                LogWithTime.LogError("[AuthManager] Error al refrescar token: " + request.downloadHandler.text);
            }
        }
    }

    public string GetServerIdToken() => idToken;
    #endregion

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
        // Pequeño respiro de un frame por seguridad
        yield return null;

        // 1) Conectar a Mirror (si no está ya activo)
        if (!NetworkClient.isConnected && !NetworkClient.active)
        {
            try
            {
                NetworkManager.singleton.StartClient();
            }
            catch (System.Exception ex)
            {
                LogWithTime.LogError($"[AuthManager] Error al iniciar cliente: {ex.Message}");
                OnConnectionFailed();
                yield break;
            }
        }

        // 2) Esperar conexión (timeout)
        float connectTimeout = 8f;
        float t = 0f;
        while (!NetworkClient.isConnected && t < connectTimeout)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!NetworkClient.isConnected)
        {
            LogWithTime.LogError("[AuthManager] Timeout de conexión a Mirror.");
            NetworkManager.singleton.StopClient();
            OnConnectionFailed();
            yield break;
        }

        // 3) Obtener la conexión y preparar UID
        var conn = NetworkClient.connection;
        if (conn == null)
        {
            LogWithTime.LogError("[AuthManager] NetworkClient.connection es null.");
            OnConnectionFailed();
            yield break;
        }

        // UID llega del dashboard -> userId
        string uid = userId;
        if (string.IsNullOrEmpty(uid)) uid = WebGLStorage.LoadString("local_id");

        // Esperar un poco a que se recupere de WebGLStorage
        float waitStart = Time.unscaledTime;
        while (string.IsNullOrEmpty(uid) && Time.unscaledTime - waitStart < 5f)
        {
            uid = WebGLStorage.LoadString("local_id");
            yield return null;
        }

        if (string.IsNullOrEmpty(uid))
        {
            LogWithTime.LogError("[AuthManager] UID vacío; abortando envío de credenciales.");
            NetworkManager.singleton.StopClient();
            OnConnectionFailed();
            yield break;
        }

        // 4) Enviar credenciales al server
        try
        {
            conn.Send(new FirebaseCredentialMessage { uid = uid });
            LogWithTime.Log($"[CLIENT] FirebaseCredentialMessage enviado (UID={uid}).");
        }
        catch (System.Exception ex)
        {
            LogWithTime.LogError($"[AuthManager] Error enviando credenciales: {ex.Message}");
            NetworkManager.singleton.StopClient();
            OnConnectionFailed();
            yield break;
        }

        // 5) Esperar ACK del server (LoginResultMessage) o cortar por timeout/desconexión
        // (OnLoginResult() debe poner loginAccepted = true al recibir OK)
        float ackTimeout = 6f;
        t = 0f;
        while (!loginAccepted && NetworkClient.isConnected && t < ackTimeout)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!loginAccepted)
        {
            // Si fue duplicado, OnLoginResult ya habrá mostrado el error y desconectado.
            if (NetworkClient.isConnected) NetworkManager.singleton.StopClient();
            OnConnectionFailed();
            yield break;
        }

        // 6) Éxito: seguimos; la UI/overlay se apaga en OnLoginResult(ok)
        yield break;
    }


    private void OnConnectionFailed()
    {
        //ShowLoginPanel();

        if (feedbackText != null)
        {
            feedbackText.text = "No se pudo conectar al servidor. Intenta nuevamente.";
        }
    }

    private IEnumerator RegisterUser(string email, string password)
    {
        string url = string.Format(SignUpUrl, firebaseWebAPIKey);

        string jwtToken = WebGLStorage.LoadString("jwt_token");

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

    private bool isLoggingOut = false;

    public void Logout()
    {
        if (isLoggingOut) return;
        isLoggingOut = true;

        if (refreshRoutine != null) { StopCoroutine(refreshRoutine); refreshRoutine = null; }

        // Limpiar almacenamiento
        WebGLStorage.DeleteKey("jwt_token");
        WebGLStorage.DeleteKey("refresh_token");
        WebGLStorage.DeleteKey("local_id");
        WebGLStorage.DeleteKey("email");

        // Limpiar estado interno
        idToken = null;
        refreshToken = null;
        userId = null;

        StartCoroutine(DelayerShowLoginPanel());
        feedbackText.text = "Sesión cerrada.";
    }

    private IEnumerator DelayerShowLoginPanel()
    {
        yield return new WaitForSecondsRealtime(1f);
        AudioManager.Instance.PlayMusic("OfflineTheme");
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

    // ==== MÉTODOS DEL BRIDGE JAVASCRIPT ====

    [System.Serializable]
    public class BridgeUserData
    {
        public string uid;
        public string email;
        public bool emailVerified;
        public string displayName;
        public string photoURL;
        public BridgeCustomClaims customClaims;
    }

    [System.Serializable]
    public class BridgeCustomClaims
    {
        public string role;
        public string walletAddress;
        public bool hasNFT;
        public long nftVerifiedAt;
        public string gameUrl;
    }
}

internal class SerializableAttribute : Attribute
{
}

