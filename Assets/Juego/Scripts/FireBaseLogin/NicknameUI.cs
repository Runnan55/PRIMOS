using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class NicknameUI : MonoBehaviour
{
    public TMP_InputField nicknameInput;
    public string feedbackText;
    public FirestoreUserUpdater firestoreUpdater;

    public void SaveNickname()
    {
        var data = new Dictionary<string, string> {
            { "nickname", nicknameInput.text.Trim() },
            { "score", "300"}
        };

        firestoreUpdater.UpdateUserData(data, result => feedbackText = result);
    }
}