using UnityEngine;
using TMPro;

public class TextManagerUI : MonoBehaviour
{
    [Header("QuickMissions")]
    public TMP_Text QM_titleText;
    public TMP_Text QM_descriptionText;

    [Header("Reward")]
    public TMP_Text Reward_titleText;
    public TMP_Text Reward_descriptionText;

    [Header("GameModeDescription")]
    public TMP_Text GM_titleText;
    public TMP_Text GM_descriptionText;

    public void SetQuickMissionText(string key)
    {
        switch (key)
        {
            case "BlockShot":
                QM_titleText.text = "QUICK MISSION";
                QM_descriptionText.text = "Block a shot successfully";
                break;
            case "DealDamage":
                QM_titleText.text = "QUICK MISSION";
                QM_descriptionText.text = "Shoot a player";
                break;
            case "DoNothing":
                QM_titleText.text = "QUICK MISSION";
                QM_descriptionText.text = "Do nothing ;)";
                break;
            case "ReloadAndTakeDamage":
                QM_titleText.text = "QUICK MISSION";
                QM_descriptionText.text = "Reload and gets shot";
                break;

        }
    }

    public void SetQuickMissionRewardText(string key)
    {
        switch (key)
        {
            case "BlockShot":
                Reward_titleText.text = "MISSION COMPLETE";
                Reward_descriptionText.text = "You get 1 life";
                break;
            case "DealDamage":
                Reward_titleText.text = "MISSION COMPLETE";
                Reward_descriptionText.text = "You get 2 bullets";
                break;
            case "DoNothing":
                Reward_titleText.text = "MISSION COMPLETE";
                Reward_descriptionText.text = "You deal double damage on your next turn";
                break;
            case "ReloadAndTakeDamage":
                Reward_titleText.text = "MISSION COMPLETE";
                Reward_descriptionText.text = "You recharged your shields";
                break;

        }
    }
    public void SetGlobalMissionFromId(string id)
    {
        switch (id)
        {
            case "CaceriaDelLider":
                GM_titleText.text = "Hunt for the leader";
                GM_descriptionText.text = "The player(s) with the most lives lose the ability to cover"; //El o los jugadores con mas vidas pierden la capacidad de cubrirse
                break;
            case "GatilloFacil":
                GM_titleText.text = "Quick Trigger";
                GM_descriptionText.text = "The players get a bullet extra at the start of the game"; //Los jugadores obtienen una bala mas al iniciar la partida
                break;
            case "BalasOxidadas":
                GM_titleText.text = "Rusty Bullets";
                GM_descriptionText.text = "All shoots have a 25% chance of missing this game"; //Todos los disparos tienen un 25% de fallar esta partida
                break;
            case "CargaOscura":
                GM_titleText.text = "Dark Chargue";
                GM_descriptionText.text = "Reload 2 bullets instead of 1"; //Recarga 2 balas en lugar de 1
                break;
        }
    }
}

