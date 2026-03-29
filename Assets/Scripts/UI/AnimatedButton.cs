using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;

[RequireComponent(typeof(Button))] 
public class AnimatedButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Colors (Tints)")]
    [Tooltip("Drag Images or Text elements here. The script will multiply their original colors by these tints.")]
    [SerializeField] private Graphic[] graphicsToTint;
    [SerializeField] private Color normalTint = Color.white;
    [SerializeField] private Color hoverTint = new Color(0.9f, 0.9f, 0.9f, 1f);
    [SerializeField] private Color clickTint = new Color(0.7f, 0.7f, 0.7f, 1f);

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
    private bool _isHovering = false;
    
    // Dictionary to remember the exact starting color of every graphic
    private Dictionary<Graphic, Color> _baseColors = new Dictionary<Graphic, Color>();

    private void Awake()
    {
        if (targetImage == null) targetImage = GetComponent<Image>();
        _button = GetComponent<Button>();
        
        _originalScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
        _button.transition = Selectable.Transition.None;

        // Cache the original colors of all graphics before we do any tinting
        if (graphicsToTint != null)
        {
            foreach (var g in graphicsToTint)
            {
                if (g != null && !_baseColors.ContainsKey(g))
                {
                    _baseColors.Add(g, g.color);
                }
            }
        }
        
        ResetVisuals(instant: true);
    }

    private void OnDisable()
    {
        transform.DOKill();
        transform.localScale = _originalScale;
        _isHovering = false;
        ResetVisuals(instant: true);
    }

    private void ResetVisuals(bool instant)
    {
        // Reset Sprite
        if (normalSprite != null && targetImage != null) targetImage.sprite = normalSprite;
        
        // Reset GameObjects
        if (normalIconObj != null) normalIconObj.SetActive(true);
        if (hoverIconObj != null) hoverIconObj.SetActive(false);

        // Apply Normal Tint
        ApplyTint(normalTint, instant);
    }

    private void ApplyTint(Color stateTint, bool instant)
    {
        if (graphicsToTint == null) return;

        foreach (var graphic in graphicsToTint)
        {
            if (graphic == null) continue;

            // Retrieve the original base color. If not found, default to white.
            Color baseColor = _baseColors.ContainsKey(graphic) ? _baseColors[graphic] : Color.white;
            
            // Multiply the base color by the state tint
            Color finalColor = baseColor * stateTint;

            graphic.DOKill();

            if (instant || !graphic.gameObject.activeInHierarchy)
            {
                graphic.color = finalColor;
            }
            else
            {
                graphic.DOColor(finalColor, animDuration).SetUpdate(true);
            }
        }
    }

    // --- Pointer Interfaces ---

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_button.interactable) return;
        _isHovering = true;
        
        // Apply Hover Visuals
        if (hoverSprite != null && targetImage != null) targetImage.sprite = hoverSprite;
        if (normalIconObj != null) normalIconObj.SetActive(false);
        if (hoverIconObj != null) hoverIconObj.SetActive(true);
        
        ApplyTint(hoverTint, instant: false);

        transform.DOKill();
        transform.DOScale(_originalScale * hoverScale, animDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_button.interactable) return;
        _isHovering = false;

        ResetVisuals(instant: false);
        
        transform.DOKill();
        transform.DOScale(_originalScale, animDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!_button.interactable) return;

        ApplyTint(clickTint, instant: false);

        transform.DOKill();
        transform.DOScale(_originalScale * clickScale, animDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_button.interactable) return;

        // Return to hover tint since the mouse is still over the button after clicking
        ApplyTint(_isHovering ? hoverTint : normalTint, instant: false);

        transform.DOKill();
        transform.DOScale(_originalScale * (_isHovering ? hoverScale : 1f), animDuration).SetEase(Ease.OutBack).SetUpdate(true);
    }
}