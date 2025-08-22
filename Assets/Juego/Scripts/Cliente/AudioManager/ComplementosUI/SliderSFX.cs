using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SliderSFX : MonoBehaviour, IBeginDragHandler, IEndDragHandler

{
    [Header("Tic durante el arrastre")]
    public float tickCooldown = 0.05f;
    private float _lastTick;

    private Slider _slider;

    void Awake()
    {
        _slider = GetComponent<Slider>();
    }

    public void OnBeginDrag(PointerEventData _) { AudioManager.Instance?.PlaySFX("StoneCrack"); }
    public void OnEndDrag(PointerEventData _) { AudioManager.Instance?.PlaySFX("StoneCrack"); }
}
