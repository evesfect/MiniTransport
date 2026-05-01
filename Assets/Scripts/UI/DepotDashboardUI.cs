using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class DepotDashboardUI : MonoBehaviour
{
    [Header("Depot Selection")]
    public TMP_Dropdown depotDropdown;
    private List<string> _availableDepotIDs = new List<string>();

    [Header("Capacity Visualizer")]
    public Image capacityFillBar;
    public TMP_Text capacityText;
    public TMP_Text statusWarningText;

    [Header("Mechanics List")]
    public Transform mechanicsListContainer;
    public GameObject mechanicRowPrefab;

    [Header("HR Actions")]
    public Button requestMechanicButton;

    private void OnEnable()
    {
        PopulateDepotDropdown();
        depotDropdown.onValueChanged.AddListener(OnDepotSelected);

        if (requestMechanicButton != null)
            requestMechanicButton.onClick.AddListener(OnRequestMechanicClicked);

        // Auto-refresh if a bus breaks down while staring at the menu!
        if (MaintenanceManager.Instance != null)
        {
            MaintenanceManager.Instance.OnWorkQueueChanged += RefreshCurrentDepot;
        }
    }

    private void OnDisable()
    {
        depotDropdown.onValueChanged.RemoveListener(OnDepotSelected);

        if (requestMechanicButton != null)
            requestMechanicButton.onClick.RemoveListener(OnRequestMechanicClicked);

        if (MaintenanceManager.Instance != null)
        {
            MaintenanceManager.Instance.OnWorkQueueChanged -= RefreshCurrentDepot;
        }
    }

    private void PopulateDepotDropdown()
    {
        depotDropdown.ClearOptions();
        _availableDepotIDs.Clear();

        var depots = FindObjectsByType<DepotController>(FindObjectsSortMode.None);
        if (depots.Length == 0) return;

        List<string> dropdownLabels = new List<string>();
        foreach (var depot in depots)
        {
            _availableDepotIDs.Add(depot.depotID);
            dropdownLabels.Add($"Depot: {depot.depotID}");
        }

        depotDropdown.AddOptions(dropdownLabels);
        OnDepotSelected(0);
    }

    private void OnDepotSelected(int index)
    {
        if (_availableDepotIDs.Count == 0 || index < 0 || index >= _availableDepotIDs.Count) return;
        RefreshDepotData(_availableDepotIDs[index]);
    }

    public void RefreshCurrentDepot()
    {
        if (_availableDepotIDs.Count > 0)
        {
            RefreshDepotData(_availableDepotIDs[depotDropdown.value]);
        }
    }

    private void RefreshDepotData(string depotID)
    {
        if (EmployeeManager.Instance == null || MaintenanceManager.Instance == null || FleetManager.Instance == null) return;

        // 1. Clean out the old mechanics list
        foreach (Transform child in mechanicsListContainer) Destroy(child.gameObject);

        // 2. Calculate SUPPLY (Total Mechanic Skill)
        float totalSupply = 0f;
        foreach (var emp in EmployeeManager.Instance.allEmployees)
        {
            if (emp.Role == EmployeeRole.Mechanic && emp.AssignedDepotID == depotID)
            {
                totalSupply += emp.SkillLevel;
                GameObject newRow = Instantiate(mechanicRowPrefab, mechanicsListContainer);
                newRow.GetComponent<MechanicUIRow>()?.Setup(emp.FullName, emp.SkillLevel);
            }
        }

        // 3. Calculate DEMAND (Total Workload for this Depot)
        float totalDemand = 0f;
        foreach (var workItem in MaintenanceManager.Instance.WorkQueue)
        {
            var bus = FleetManager.Instance.allBuses.FirstOrDefault(b => b.BusID == workItem.BusID);
            if (bus != null && bus.AssignedDepotID == depotID)
            {
                // Note: Ensure your MaintenanceManager has a method like GetMaxCapacityAllowance
                // or just read the required capacity directly from the workItem.
                totalDemand += MaintenanceManager.Instance.GetMaxCapacityAllowance(workItem.IssuePartType);
            }
        }

        // 4. Update Visuals
        UpdateCapacityVisuals(totalSupply, totalDemand);
    }

    private void UpdateCapacityVisuals(float supply, float demand)
    {
        float safeSupply = supply == 0 ? 0.1f : supply; // Prevent divide by zero
        float loadPercentage = demand / safeSupply;

        if (capacityFillBar != null)
        {
            capacityFillBar.fillAmount = Mathf.Clamp01(loadPercentage);

            // Visual Storytelling colors
            if (loadPercentage < 0.6f) capacityFillBar.color = Color.green;
            else if (loadPercentage <= 1.0f) capacityFillBar.color = Color.yellow;
            else capacityFillBar.color = Color.red;
        }

        if (capacityText != null)
            capacityText.text = $"Workload: {demand:F0} / {supply:F0} Cap";

        if (statusWarningText != null)
        {
            if (demand == 0)
            {
                statusWarningText.text = "Status: All Clear";
                statusWarningText.color = Color.gray;
            }
            else if (loadPercentage > 1.2f)
            {
                statusWarningText.text = "Status: SEVERELY UNDERSTAFFED!";
                statusWarningText.color = Color.red;
            }
            else if (loadPercentage > 0.9f)
            {
                statusWarningText.text = "Status: At Maximum Capacity";
                statusWarningText.color = new Color(1f, 0.5f, 0f); // Orange
            }
            else
            {
                statusWarningText.text = "Status: Operational";
                statusWarningText.color = Color.green;
            }
        }
    }

    private void OnRequestMechanicClicked()
    {
        string currentDepot = _availableDepotIDs[depotDropdown.value];
        Debug.Log($"[UI] Requesting HR to hire a new mechanic for {currentDepot}...");
        // Call your HR Manager script here to fire the notification/hire sequence
    }
}