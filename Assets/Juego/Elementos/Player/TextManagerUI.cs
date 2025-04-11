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

    public void SetMissionText(string key)
    {
        switch (key)
        {
            case "BlockShot":
                QM_titleText.text = "MISION RAPIDA";
                QM_descriptionText.text = "Bloquea un disparo exitosamente";
                break;
            case "DealDamage":
                QM_titleText.text = "MISION RAPIDA";
                QM_descriptionText.text = "Ataca a un jugador";
                break;
            case "DoNothing":
                QM_titleText.text = "MISION RAPIDA";
                QM_descriptionText.text = "No hagas nada ;)";
                break;
            case "ReloadAndTakeDamage":
                QM_titleText.text = "MISION RAPIDA";
                QM_descriptionText.text = "Recarga y recibe un ataque";
                break;

        }
    }

    public void SetRewardText(string key)
    {
        switch (key)
        {
            case "BlockShot":
                Reward_titleText.text = "MISION CUMPLIDA";
                Reward_descriptionText.text = "Recibes 1 vida";
                break;
            case "DealDamage":
                Reward_titleText.text = "MISION CUMPLIDA";
                Reward_descriptionText.text = "Recibes 2 balas";
                break;
            case "DoNothing":
                Reward_titleText.text = "MISION CUMPLIDA";
                Reward_descriptionText.text = "Realizas el doble de golpes en tu siguiente turno";
                break;
            case "ReloadAndTakeDamage":
                Reward_titleText.text = "MISION CUMPLIDA";
                Reward_descriptionText.text = "Recargaste tus escudos";
                break;

        }
    }
}
