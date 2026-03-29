using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CanvasGroup))]
public class BasePanel : MonoBehaviour
{
    [Header("Input & Controls")]
    [Tooltip("The input action that toggles this panel (e.g., 'I' for Inspection)")]
    [SerializeField] private InputActionReference toggleInput;
    [Tooltip("The 'X' button in the top right of your panel")]
    [SerializeField] private Button closeButton;

    [Header("Panel Behavior")]
    [Tooltip("If true, opening this panel closes other exclusive panels.")]
    public bool isExclusive = true; 

    private CanvasGroup _canvasGroup;
    public bool IsOpen { get; private set; }

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        
        // Ensure the panel starts fully hidden and non-interactable without disabling the GameObject
        Hide(); 

        // Bind the UI button
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(() => UIManager.Instance.ClosePanel(this));
        }
    }

    private void OnEnable()
    {
        if (toggleInput != null)
        {
            toggleInput.action.Enable();
            toggleInput.action.performed += OnTogglePerformed;
        }
    }

    private void OnDisable()
    {
        if (toggleInput != null)
        {
            toggleInput.action.performed -= OnTogglePerformed;
            toggleInput.action.Disable();
        }
    }

    private void OnTogglePerformed(InputAction.CallbackContext ctx)
    {
        // Don't open/close directly. Ask the manager to handle it.
        if (UIManager.Instance != null)
        {
            UIManager.Instance.RequestToggle(this);
        }
    }

    // --- Visibility State (Called ONLY by UIManager) ---

    public void Show()
    {
        IsOpen = true;
        _canvasGroup.alpha = 1f;
        _canvasGroup.interactable = true;
        _canvasGroup.blocksRaycasts = true;
        transform.SetAsLastSibling(); // Brings this panel to the front of the Canvas
    }

    public void Hide()
    {
        IsOpen = false;
        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;
    }

    public void SetPanelState(bool shouldBeOpen)
    {
        if (UIManager.Instance == null) return;

        if (shouldBeOpen && !IsOpen)
        {
            UIManager.Instance.RequestToggle(this); 
        }
        else if (!shouldBeOpen && IsOpen)
        {
            UIManager.Instance.ClosePanel(this);
        }
    }
}