using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using Mirror;
using System;
using System.Collections.Generic;

public class FirestoreUserManager : MonoBehaviour
{
    [Header("Firebase Project ID")]
    [SerializeField] private string firebaseProjectId = "primosminigameshoot"; // ID real de tu proyecto

    [Header("Nickname UI")]
    public TMP_InputField nicknameInput;

    [Header("Feedback")]
    public TMP_Text feedbackText;

    public void SaveUser(string nickname)
    {
        string idToken = WebGLStorage.LoadString("jwt_token");
        string uid = WebGLStorage.LoadString("local_id");
        string email = WebGLStorage.LoadString("email");

        if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(email))
        {
            feedbackText.text = "Faltan datos para guardar en Firestore.";
            return;
        }

        StartCoroutine(SendUserDataToFirestore(idToken, uid, email, nickname));
    }

    public void LoadUser()
    {
        string idToken = WebGLStorage.LoadString("jwt_token");
        string uid = WebGLStorage.LoadString("local_id");

        if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(uid))
        {
            feedbackText.text = "No hay sesión activa para cargar.";
            return;
        }

        StartCoroutine(LoadUserFromFirestore(idToken, uid));
    }

    private IEnumerator SendUserDataToFirestore(string idToken, string uid, string email, string nickname)
    {
        string url = $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents/users/{uid}";

        string jsonPayload = JsonUtility.ToJson(new FirestoreUser
        {
            fields = new FirestoreUserFields
            {
                uid = new FirestoreString { stringValue = uid },
                email = new FirestoreString { stringValue = email },
                nickname = new FirestoreString { stringValue = nickname },
                rankedPoints = new FirestoreInteger { integerValue = "0" }
            }
        });

        UnityWebRequest request = new UnityWebRequest(url, "PATCH");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + idToken);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
            feedbackText.text = "Error al guardar usuario: " + request.downloadHandler.text;
        else
            feedbackText.text = "Usuario guardado exitosamente.";
    }

    private IEnumerator LoadUserFromFirestore(string idToken, string uid)
    {
        string url = $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents/users/{uid}";

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", "Bearer " + idToken);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            feedbackText.text = "Error al cargar usuario: " + request.downloadHandler.text;
        }
        else
        {
            string response = request.downloadHandler.text;
            var doc = JsonUtility.FromJson<FirestoreDocument>(response);
            string nickname = doc.fields.nickname.stringValue;
            nicknameInput.text = nickname;
            feedbackText.text = $"Firestore cargado OK\nNick: {nickname}";

            // NUEVO: Enviar nombre al servidor después de cargarlo
            yield return new WaitForSeconds(0.1f); // Pequeño delay para asegurar que el jugador fue registrado
            if (NetworkClient.isConnected && NetworkClient.connection != null)
            {
                NetworkClient.connection.Send(new NameMessage { playerName = nickname });
                Debug.Log($"[FirestoreUserManager] Nickname enviado al servidor tras carga: {nickname}");
            }
        }
    }

    #region Ticket y Llaves

    public IEnumerator GetWalletData(string uid, string idToken, Action<bool, Dictionary<string, object>> callback)
    {
        Debug.Log($"[Firestore DEBUG] UID desde WebGLStorage: {uid}");
        Debug.Log($"[Firestore DEBUG] Token inicia con: {idToken.Substring(0, 30)}"); // solo muestra los primeros caracteres por seguridad

        // 1. Obtener el documento del usuario para extraer activeWallet
        string userUrl = $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents/users/{uid}";

        UnityWebRequest userRequest = UnityWebRequest.Get(userUrl);
        userRequest.SetRequestHeader("Authorization", "Bearer " + idToken);
        yield return userRequest.SendWebRequest();

        if (userRequest.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[Firestore] Error al obtener user: {userRequest.error} - {userRequest.downloadHandler.text}");
            callback(false, null);
            yield break;
        }

        string userJson = userRequest.downloadHandler.text;
        var userParsed = SimpleJSON.JSON.Parse(userJson);
        string walletAddress = userParsed["fields"]["activeWallet"]?["stringValue"];
        Debug.Log($"[Firestore DEBUG] Wallet extraída del user: {walletAddress}");

        if (string.IsNullOrEmpty(walletAddress))
        {
            Debug.LogWarning("[Firestore] No se encontró campo 'activeWallet' en el documento del usuario.");
            callback(false, null);
            yield break;
        }

        // 2. Obtener el documento de la wallet usando el walletAddress
        string walletUrl = $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents/wallets/{walletAddress}";
        UnityWebRequest walletRequest = UnityWebRequest.Get(walletUrl);
        walletRequest.SetRequestHeader("Authorization", "Bearer " + idToken);
        yield return walletRequest.SendWebRequest();

        if (walletRequest.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[Firestore] Error al obtener wallet: {walletRequest.error} - {walletRequest.downloadHandler.text}");
            callback(false, null);
            yield break;
        }

        try
        {
            var json = walletRequest.downloadHandler.text;
            var parsed = SimpleJSON.JSON.Parse(json);

            Dictionary<string, object> result = new Dictionary<string, object>();

            // Extraer ticketsAvailable
            var tickets = parsed["fields"]["ticketsAvailable"]["integerValue"];
            result["ticketsAvailable"] = string.IsNullOrEmpty(tickets) ? 0 : int.Parse(tickets);

            // Extraer llaves (gameBalance > keys > basic)
            var basicKeys = parsed["fields"]["gameBalance"]["mapValue"]["fields"]["keys"]["mapValue"]["fields"]["basic"]["integerValue"];
            result["llavesBasic"] = string.IsNullOrEmpty(basicKeys) ? 0 : int.Parse(basicKeys);

            callback(true, result);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Firestore] Excepción al parsear wallet: {ex.Message}");
            callback(false, null);
        }
    }

    #endregion

    [System.Serializable] public class FirestoreUser { public FirestoreUserFields fields; }
    [System.Serializable]
    public class FirestoreUserFields
    {
        public FirestoreString uid;
        public FirestoreString email;
        public FirestoreString nickname;
        public FirestoreInteger rankedPoints;
    }
    [System.Serializable] public class FirestoreInteger { public string integerValue; }
    [System.Serializable] public class FirestoreString { public string stringValue; }
    [System.Serializable] public class FirestoreDocument { public FirestoreUserFields fields; }
}
