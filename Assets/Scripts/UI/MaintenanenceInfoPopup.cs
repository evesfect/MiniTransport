using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

public class MaintenanceInfoPopup : MonoBehaviour
{
    [Header("UI Text")]
    public TMP_Text popupTitleText;
    public TMP_Text popupIssueText;

    [Header("Health Fill Bars")]
    public Image engineBar;
    public Image transmissionBar;
    public Image wheelsBar;
    public Image bodyBar;
    public Image interiorBar;

    [Header("Capacity Breakdown Texts")]
    // NEW: Assign these in the inspector to sit next to your health bars
    public TMP_Text engineDemandText;
    public TMP_Text transmissionDemandText;
    public TMP_Text wheelsDemandText;
    public TMP_Text bodyDemandText;
    public TMP_Text interiorDemandText;

    public void ClosePopup()
    {
        gameObject.SetActive(false);
    }

    public void Show(WorkItem workItem)
    {
        var bus = FleetManager.Instance.allBuses.FirstOrDefault(b => b.BusID == workItem.BusID);
        if (bus == null || MaintenanceManager.Instance == null) return;

        popupTitleText.text = $"Bus {bus.BusID} Diagnostics";

        float totalDemand = 0f;
        float replaceThreshold = MaintenanceManager.Instance.replacePartThreshold;

        // Loop through and calculate the breakdown
        foreach (var part in bus.Parts)
        {
            float healthPct = Mathf.Clamp01(part.Health / 100f);
            float partDemand = 0f;

            // If the part is damaged, grab its specific capacity cost
            if (part.Health < part.MaxLife || part.MaxLife < replaceThreshold)
            {
                partDemand = MaintenanceManager.Instance.GetMaxCapacityAllowance(part.PartType);
                totalDemand += partDemand;
            }

            // Format the string: Show the cost, or show "OK" if it doesn't need repairs
            string demandString = partDemand > 0 ? $"{partDemand:F0} Cap" : "OK";

            switch (part.PartType)
            {
                case BusPartType.Engine:
                    UpdateBar(engineBar, healthPct);
                    if (engineDemandText) engineDemandText.text = demandString;
                    break;
                case BusPartType.Transmission:
                    UpdateBar(transmissionBar, healthPct);
                    if (transmissionDemandText) transmissionDemandText.text = demandString;
                    break;
                case BusPartType.Tires:
                    UpdateBar(wheelsBar, healthPct);
                    if (wheelsDemandText) wheelsDemandText.text = demandString;
                    break;
                case BusPartType.Body:
                    UpdateBar(bodyBar, healthPct);
                    if (bodyDemandText) bodyDemandText.text = demandString;
                    break;
                case BusPartType.Interior:
                    UpdateBar(interiorBar, healthPct);
                    if (interiorDemandText) interiorDemandText.text = demandString;
                    break;
            }
        }

        var primaryPartData = bus.Parts.FirstOrDefault(p => p.PartType == workItem.IssuePartType);
        string primaryIssueDisplay = FormatPartName(workItem.IssuePartType.ToString());

        if (primaryPartData != null)
        {
            if (!string.IsNullOrEmpty(primaryPartData.PendingReplacementItemID))
            {
                primaryIssueDisplay = FormatPartName(primaryPartData.PendingReplacementItemID);
            }
            else if (primaryPartData.MaxLife < replaceThreshold)
            {
                primaryIssueDisplay = $"{FormatPartName(workItem.IssuePartType.ToString())} (Replacement)";
            }
            else if (primaryPartData.Health < primaryPartData.MaxLife)
            {
                primaryIssueDisplay = $"{FormatPartName(workItem.IssuePartType.ToString())} (Repair)";
            }
        }

        popupIssueText.text = $"Primary Issue: {primaryIssueDisplay}";

        gameObject.SetActive(true);
    }

    private void UpdateBar(Image bar, float percentage)
    {
        if (bar != null)
        {
            bar.fillAmount = percentage;
            if (percentage > 0.5f) bar.color = Color.green;
            else if (percentage > 0.2f) bar.color = Color.yellow;
            else bar.color = Color.red;
        }
    }

    private string FormatPartName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return "";
        return string.Concat(rawName.Select((x, i) => i > 0 && char.IsUpper(x) ? " " + x : x.ToString()));
    }
}