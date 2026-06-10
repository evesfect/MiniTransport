using UnityEngine;

public class RequestPanelManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The empty RectTransform inside the consistent panel frame")]
    public Transform contentContainer; 
    
    [Header("Top-Level Prefabs (Spawned per Role)")]
    public GameObject maintenanceContentPrefab; // MaintenanceRequestContent.png layout
    public GameObject transportContentPrefab; // TransportRequestContent.png layout

    private BasePanel _basePanel;
    private GameObject _currentActiveContent;

    private void Awake()
    {
        _basePanel = GetComponent<BasePanel>();
    }

    private void OnEnable()
    {
        if (_basePanel != null)
        {
            _basePanel.OnPanelShown += HandlePanelOpened;
        }
    }

    private void OnDisable()
    {
        if (_basePanel != null)
        {
            _basePanel.OnPanelShown -= HandlePanelOpened;
        }
    }

    private void HandlePanelOpened()
    {
        PlayerRole myRole = RoleManager.Instance.GetMyRole();

        // Clean up previous dynamic content if it exists
        if (_currentActiveContent != null)
        {
            Destroy(_currentActiveContent);
        }

        // Spawn the correct top-level visual layout based on role
        if (myRole == PlayerRole.MaintenanceManager && maintenanceContentPrefab != null)
        {
            _currentActiveContent = Instantiate(maintenanceContentPrefab, contentContainer);
        }
        else if (myRole == PlayerRole.TransportManager && transportContentPrefab != null)
        {
            _currentActiveContent = Instantiate(transportContentPrefab, contentContainer);
        }
        
        // Fix scaling bug from code instantiation
        if (_currentActiveContent != null)
        {
            _currentActiveContent.transform.localScale = Vector3.one;
            _currentActiveContent.transform.localPosition = Vector3.zero;
        }
    }
}