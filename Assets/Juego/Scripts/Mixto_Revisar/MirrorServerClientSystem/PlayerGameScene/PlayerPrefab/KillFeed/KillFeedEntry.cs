using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class KillFeedEntry : MonoBehaviour
{
    [SerializeField] private TMP_Text killerText;
    [SerializeField] private TMP_Text victimText;

    public void Setup(string killerName, string victimName)
    {
        if (killerText) killerText.text = killerName ?? "";
        if (victimText) victimText.text = victimName ?? "";
    }
}