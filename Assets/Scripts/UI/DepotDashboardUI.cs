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

        // 2. Calculate SUPPLY (Total Mechanic Skill) and list mechanics grouped & labeled by their Team
        float totalSupply = 0f;

        var depotMechanics = EmployeeManager.Instance.allEmployees
            .Where(e => e.Role == EmployeeRole.Mechanic && e.AssignedDepotID == depotID)
            .OrderBy(e => string.IsNullOrEmpty(e.AssignedTeamID) ? "General Crew" : e.AssignedTeamID)
            .ToList();

        foreach (var emp in depotMechanics)
        {
            totalSupply += emp.SkillLevel;
            string teamName = string.IsNullOrEmpty(emp.AssignedTeamID) ? "General Crew" : emp.AssignedTeamID;

            GameObject newRow = Instantiate(mechanicRowPrefab, mechanicsListContainer);
            // Reusing your existing row component perfectly while appending the team name visually
            newRow.GetComponent<MechanicUIRow>()?.Setup($"{emp.FullName} ({teamName})", emp.SkillLevel);
        }

        // 3. Calculate DEMAND (Total Workload for this Depot)
        // Instead of relying on the single WorkItem per bus in WorkQueue, directly evaluate all broken parts
        // across all buses assigned to this depot. This guarantees an accurate, real-time demand measurement.
        float totalDemand = 0f;
        float replaceThreshold = MaintenanceManager.Instance.replacePartThreshold;
        float repairRate = MaintenanceManager.Instance.repairPerSkillPoint;

        foreach (var bus in FleetManager.Instance.allBuses)
        {
            // IGNORE buses that are currently out driving routes or unassigned
            if (bus.AssignedDepotID != depotID || bus.Parts == null) continue;
            if (FleetManager.Instance.IsBusActive(bus.BusID)) continue;

            foreach (var part in bus.Parts)
            {
                float maxAllowance = MaintenanceManager.Instance.GetMaxCapacityAllowance(part.PartType);

                // A. REPLACEMENT: Needs full allocated capacity allowance to swap the part
                if (part.MaxLife < replaceThreshold)
                {
                    totalDemand += maxAllowance;
                }
                // B. REPAIR: Only count the actual capacity required to top-up the health
                else if (part.Health < part.MaxLife)
                {
                    float missingHealth = part.MaxLife - part.Health;
                    float capacityNeededToHeal = missingHealth / (repairRate > 0 ? repairRate : 1f);

                    // Clamp it so a repair demand never exceeds the hourly bottleneck allowance
                    totalDemand += Mathf.Min(capacityNeededToHeal, maxAllowance);
                }
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