using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;

[RequireComponent(typeof(Image))]
[RequireComponent(typeof(Button))] 
public class AnimatedButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Sprite Swapping (Optional)")]
    [Tooltip("The Image component that will swap sprites.")]
    [SerializeField] private Image targetImage;
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite hoverSprite;

    [Header("GameObject Toggling (Optional)")]
    [Tooltip("The default icon GameObject (will be disabled on hover).")]
    [SerializeField] private GameObject normalIconObj;
    [Tooltip("The hover icon GameObject (will be enabled on hover).")]
    [SerializeField] private GameObject hoverIconObj;

    [Header("DOTween Settings")]
    [SerializeField] private float hoverScale = 1.05f;
    [SerializeField] private float clickScale = 0.95f;
    [SerializeField] private float animDuration = 0.15f;

    private Vector3 _originalScale;
    private Button _button;

    private void Awake()
    {
        if (targetImage == null) targetImage = GetComponent<Image>();
        _button = GetComponent<Button>();
        
        // Failsafe: If scale is 0 because of a weird canvas initialization, default to 1
        _originalScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
        
        _button.transition = Selectable.Transition.None;
        
        ResetVisuals();
    }

    private void OnDisable()
    {
        // Prevent DOTween from getting stuck if the panel closes mid-animation
        transform.DOKill();
        transform.localScale = _originalScale;
        ResetVisuals();
    }

    private void ResetVisuals()
    {
        // Reset Sprite
        if (normalSprite != null && targetImage != null) targetImage.sprite = normalSprite;
        
        // Reset GameObjects
        if (normalIconObj != null) normalIconObj.SetActive(true);
        if (hoverIconObj != null) hoverIconObj.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_button.interactable) return;
        
        // Apply Hover Visuals
        if (hoverSprite != null && targetImage != null) targetImage.sprite = hoverSprite;
        if (normalIconObj != null) normalIconObj.SetActive(false);
        if (hoverIconObj != null) hoverIconObj.SetActive(true);
        
        transform.DOKill();
        transform.DOScale(_originalScale * hoverScale, animDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_button.interactable) return;

        ResetVisuals();
        
        transform.DOKill();
        transform.DOScale(_originalScale, animDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!_button.interactable) return;

        transform.DOKill();
        transform.DOScale(_originalScale * clickScale, animDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_button.interactable) return;

        transform.DOKill();
        // Pop back to the hover scale since the mouse is still technically over the button
        transform.DOScale(_originalScale * hoverScale, animDuration).SetEase(Ease.OutBack).SetUpdate(true);
    }
}