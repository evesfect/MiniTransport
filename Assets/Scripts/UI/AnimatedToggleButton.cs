using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using DG.Tweening;

[RequireComponent(typeof(Image))]
[RequireComponent(typeof(Button))] 
public class AnimatedToggleButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Toggle State")]
    public bool isOn = false;
    public UnityEvent<bool> onValueChanged;

    [Header("Colors")]
    [Tooltip("Leave as white if you only want to use Sprites/GameObjects without tinting them.")]
    [SerializeField] private Color offNormalColor = Color.white;
    [SerializeField] private Color offHoverColor = new Color(0.9f, 0.9f, 0.9f, 1f);
    [SerializeField] private Color onNormalColor = Color.white;
    [SerializeField] private Color onHoverColor = new Color(0.9f, 0.9f, 0.9f, 1f);
    
    [Tooltip("Drag any child Images or Text elements here that should also change color.")]
    [SerializeField] private Graphic[] additionalGraphicsToTint;

    [Header("Sprites (Optional)")]
    [SerializeField] private Image targetImage;
    [SerializeField] private Sprite offNormalSprite;
    [SerializeField] private Sprite offHoverSprite;
    [SerializeField] private Sprite onNormalSprite;
    [SerializeField] private Sprite onHoverSprite;

    [Header("GameObjects (Optional)")]
    [SerializeField] private GameObject offNormalObj;
    [SerializeField] private GameObject offHoverObj;
    [SerializeField] private GameObject onNormalObj;
    [SerializeField] private GameObject onHoverObj;

    [Header("DOTween Settings")]
    [SerializeField] private float hoverScale = 1.05f;
    [SerializeField] private float clickScale = 0.95f;
    [SerializeField] private float animDuration = 0.15f;

    private Vector3 _originalScale;
    private Button _button;
    private bool _isHovering = false;

    private void Awake()
    {
        if (targetImage == null) targetImage = GetComponent<Image>();
        _button = GetComponent<Button>();
        
        _originalScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
        _button.transition = Selectable.Transition.None;

        _button.onClick.AddListener(ToggleState);
        UpdateVisuals(instant: true);
    }

    private void OnDisable()
    {
        transform.DOKill();
        if (targetImage != null) targetImage.DOKill();
        
        if (additionalGraphicsToTint != null)
        {
            foreach (var g in additionalGraphicsToTint) if (g != null) g.DOKill();
        }
        
        transform.localScale = _originalScale;
        _isHovering = false;
        UpdateVisuals(instant: true);
    }

    public void ToggleState()
    {
        SetState(!isOn);
    }

    public void SetState(bool newState, bool notify = true)
    {
        isOn = newState;
        UpdateVisuals(instant: false);
        
        if (notify)
        {
            onValueChanged?.Invoke(isOn);
        }
    }

    private void UpdateVisuals(bool instant)
    {
        // 1. Disable all GameObjects first
        if (offNormalObj != null) offNormalObj.SetActive(false);
        if (offHoverObj != null) offHoverObj.SetActive(false);
        if (onNormalObj != null) onNormalObj.SetActive(false);
        if (onHoverObj != null) onHoverObj.SetActive(false);

        // 2. Determine target states
        Color targetColor;

        if (isOn)
        {
            targetColor = _isHovering ? onHoverColor : onNormalColor;
            
            if (targetImage != null && onNormalSprite != null) 
                targetImage.sprite = _isHovering && onHoverSprite != null ? onHoverSprite : onNormalSprite;

            if (_isHovering && onHoverObj != null) onHoverObj.SetActive(true);
            else if (onNormalObj != null) onNormalObj.SetActive(true);
        }
        else
        {
            targetColor = _isHovering ? offHoverColor : offNormalColor;
            
            if (targetImage != null && offNormalSprite != null) 
                targetImage.sprite = _isHovering && offHoverSprite != null ? offHoverSprite : offNormalSprite;

            if (_isHovering && offHoverObj != null) offHoverObj.SetActive(true);
            else if (offNormalObj != null) offNormalObj.SetActive(true);
        }

        // 3. Apply Colors
        ApplyColor(targetImage, targetColor, instant);

        if (additionalGraphicsToTint != null)
        {
            foreach (var graphic in additionalGraphicsToTint)
            {
                ApplyColor(graphic, targetColor, instant);
            }
        }
    }

    private void ApplyColor(Graphic graphic, Color targetColor, bool instant)
    {
        if (graphic == null) return;

        graphic.DOKill();

        // If the object is turned off, DOTween will fail. Snap the color instantly instead.
        if (instant || !graphic.gameObject.activeInHierarchy)
        {
            graphic.color = targetColor;
        }
        else
        {
            graphic.DOColor(targetColor, animDuration).SetUpdate(true);
        }
    }

    // --- Pointer Interfaces ---

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_button.interactable) return;
        
        _isHovering = true;
        UpdateVisuals(instant: false);
        
        transform.DOKill();
        transform.DOScale(_originalScale * hoverScale, animDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_button.interactable) return;

        _isHovering = false;
        UpdateVisuals(instant: false);
        
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
        transform.DOScale(_originalScale * (_isHovering ? hoverScale : 1f), animDuration).SetEase(Ease.OutBack).SetUpdate(true);
    }
}