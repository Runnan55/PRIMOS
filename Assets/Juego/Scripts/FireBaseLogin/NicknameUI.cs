using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class NicknameUI : MonoBehaviour
{
    public TMP_InputField nicknameInput;
    public string feedbackText;
    //public FirestoreUserUpdater firestoreUpdater;

    public void SaveNickname()
    {
        var data = new Dictionary<string, object> {
            { "nickname", nicknameInput.text.Trim() },
        };

        var updater = FindFirstObjectByType<FirestoreUserUpdater>();

        updater.UpdateUserData(data, result => feedbackText = result);
    }
}