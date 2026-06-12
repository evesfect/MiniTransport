using UnityEngine;

/// <summary>
/// Attach this to a PERSISTENT object (like your Canvas or Bottom Bar), 
/// NOT the buttons themselves, so it stays active and listens to network events!
/// </summary>
public class RequestButtonAccess : MonoBehaviour
{
    [Header("UI Button References")]
    public GameObject requestButtonObject;
    public GameObject transportButtonObject;
    public GameObject inventoryButtonObject;
    public GameObject vendorButtonObject;
    public GameObject financeButtonObject;
    public GameObject maintenanceButtonObject;
    public GameObject gmButtonObject;
    public GameObject workItemButtonObject;
    public GameObject hrButtonObject; // [NEW]

    private void Start()
    {
        // Hide all buttons by default at the start of the game
        SetAllButtonsActive(false);
    }

    private void OnEnable()
    {
        if (RoleManager.Instance != null)
        {
            RoleManager.Instance.OnRolesUpdated += CheckAccess;
        }
    }

    private void OnDisable()
    {
        if (RoleManager.Instance != null)
        {
            RoleManager.Instance.OnRolesUpdated -= CheckAccess;
        }
    }

    private void CheckAccess()
    {
        PlayerRole myRole = RoleManager.Instance.GetMyRole();

        // 1. Request Button (Maintenance, Transport)
        if (requestButtonObject != null)
            requestButtonObject.SetActive(myRole == PlayerRole.MaintenanceManager || myRole == PlayerRole.TransportManager);

        // 2. Transport Button (Transport only)
        if (transportButtonObject != null)
            transportButtonObject.SetActive(myRole == PlayerRole.TransportManager);

        // 3. Inventory Button (Maintenance, Finance, GM)
        if (inventoryButtonObject != null)
            inventoryButtonObject.SetActive(myRole == PlayerRole.MaintenanceManager || myRole == PlayerRole.FinanceManager || myRole == PlayerRole.GeneralManager);

        // 4. Vendor Button (Finance only)
        if (vendorButtonObject != null)
            vendorButtonObject.SetActive(myRole == PlayerRole.FinanceManager);

        // 5. Finance Button (Finance only)
        if (financeButtonObject != null)
            financeButtonObject.SetActive(myRole == PlayerRole.FinanceManager);

        // 6. Maintenance Button (Maintenance only)
        if (maintenanceButtonObject != null)
            maintenanceButtonObject.SetActive(myRole == PlayerRole.MaintenanceManager);

        // 7. GM Button (GM only)
        if (gmButtonObject != null)
            gmButtonObject.SetActive(myRole == PlayerRole.GeneralManager);

        // 8. Work Item Button (Maintenance only)
        if (workItemButtonObject != null)
            workItemButtonObject.SetActive(myRole == PlayerRole.MaintenanceManager);
            
        // 9. HR Button (HR only)
        if (hrButtonObject != null)
            hrButtonObject.SetActive(myRole == PlayerRole.HRManager);
    }

    /// <summary>
    /// Helper method to safely toggle all registered buttons at once
    /// </summary>
    private void SetAllButtonsActive(bool isActive)
    {
        if (requestButtonObject != null) requestButtonObject.SetActive(isActive);
        if (transportButtonObject != null) transportButtonObject.SetActive(isActive);
        if (inventoryButtonObject != null) inventoryButtonObject.SetActive(isActive);
        if (vendorButtonObject != null) vendorButtonObject.SetActive(isActive);
        if (financeButtonObject != null) financeButtonObject.SetActive(isActive);
        if (maintenanceButtonObject != null) maintenanceButtonObject.SetActive(isActive);
        if (gmButtonObject != null) gmButtonObject.SetActive(isActive);
        if (workItemButtonObject != null) workItemButtonObject.SetActive(isActive);
        if (hrButtonObject != null) hrButtonObject.SetActive(isActive);
    }
}