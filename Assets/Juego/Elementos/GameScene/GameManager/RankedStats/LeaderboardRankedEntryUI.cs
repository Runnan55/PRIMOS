using TMPro;
using UnityEngine;

public class LeaderboardEntryUI : MonoBehaviour
{
    public TMP_Text rankText;
    public TMP_Text nameText;
    public TMP_Text pointsText;

    public void SetEntry(int rank, string playerName, int points)
    {
        rankText.text = $"{rank}.";
        nameText.text = playerName;
        pointsText.text = $"{points} pts";
    }
}
