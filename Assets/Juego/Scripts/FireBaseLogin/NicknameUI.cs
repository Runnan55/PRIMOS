using UnityEngine;
using TMPro;
using Mirror;

public class NicknameUI : MonoBehaviour
{
    public TMP_InputField nicknameInput;
    public string feedbackText;

    public void SaveNickname()
    {
        if (!NetworkClient.isConnected || NetworkClient.connection == null)
        {
            Debug.LogWarning("[NicknameUI] No conectado al servidor.");
            feedbackText = "No conectado al servidor.";
            return;
        }

        string nickname = nicknameInput.text.Trim();

        NameMessage nameMsg = new NameMessage
        {
            playerName = nickname
        };

        CustomRoomPlayer.LocalInstance.CmdUpdateNickname(nickname);
        //NetworkClient.Send(nameMsg);
        feedbackText = "Nombre enviado al servidor.";
        Debug.Log($"[NicknameUI] Enviando NameMessage con nombre: {nickname}");
    }
}