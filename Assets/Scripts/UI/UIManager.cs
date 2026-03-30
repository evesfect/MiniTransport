using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }
    
    // We use a Stack to handle Last-In-First-Out closing logic (like pressing Escape)
    private Stack<BasePanel> _openPanels = new Stack<BasePanel>();

    [Header("Global Inputs")]
    [Tooltip("Input to close the top-most panel (usually 'Escape')")]
    [SerializeField] private InputActionReference closeTopPanelInput; 

    private void Awake()
    {
        if (Instance != null && Instance != this) 
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        if (closeTopPanelInput != null)
        {
            closeTopPanelInput.action.Enable();
            closeTopPanelInput.action.performed += OnCloseTopPanelPerformed;
        }
    }

    private void OnDisable()
    {
        if (closeTopPanelInput != null)
        {
            closeTopPanelInput.action.performed -= OnCloseTopPanelPerformed;
            closeTopPanelInput.action.Disable();
        }
    }

    private void OnCloseTopPanelPerformed(InputAction.CallbackContext ctx)
    {
        CloseTopPanel();
    }

    // --- Core Routing Logic ---

    public void RequestToggle(BasePanel panel)
    {
        if (panel.IsOpen)
        {
            ClosePanel(panel);
        }
        else
        {
            OpenPanel(panel);
        }
    }

    private void OpenPanel(BasePanel panel)
    {
        // If the panel is marked exclusive, we close other exclusive panels first
        if (panel.isExclusive)
        {
            CloseAllExclusivePanels();
        }

        _openPanels.Push(panel);
        panel.Show();
        
        // TODO: Optional - Pause the game via SimulationTimeManager when a panel opens
    }

    public void ClosePanel(BasePanel panel)
    {
        if (!_openPanels.Contains(panel)) return;

        // If it's the top panel, just pop it cleanly
        if (_openPanels.Peek() == panel)
        {
            _openPanels.Pop();
        }
        else
        {
            // If a panel was closed "out of order" (e.g., clicking its specific 'X' button 
            // while another panel is technically in front of it), rebuild the stack without it.
            var tempArray = _openPanels.ToArray();
            _openPanels.Clear();
            for (int i = tempArray.Length - 1; i >= 0; i--)
            {
                if (tempArray[i] != panel) _openPanels.Push(tempArray[i]);
            }
        }

        panel.Hide();

        // TODO: Optional - Resume the game if _openPanels.Count == 0
    }

    public void CloseTopPanel()
    {
        if (_openPanels.Count > 0)
        {
            BasePanel topPanel = _openPanels.Peek();
            ClosePanel(topPanel);
        }
    }

    private void CloseAllExclusivePanels()
    {
        // Copy to array to avoid modifying the stack while iterating
        var currentPanels = _openPanels.ToArray(); 
        foreach (var p in currentPanels)
        {
            if (p.isExclusive) ClosePanel(p);
        }
    }
}