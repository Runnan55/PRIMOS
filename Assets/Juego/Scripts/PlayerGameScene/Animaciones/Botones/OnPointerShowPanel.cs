using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class OnPointerShowPanel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    //public GameObject tooltipPanel;
    public Animator playerAnimator;

    public void OnPointerEnter(PointerEventData eventData)
    {
        /*if (tooltipPanel != null)
        {
            tooltipPanel.SetActive(true);
        }*/
        playerAnimator.Play("OnPointerEnter");
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        /*if (tooltipPanel != null)
        {
            tooltipPanel.SetActive(false);
        }*/
        playerAnimator.Play("OnPointerExit");
    }
}

