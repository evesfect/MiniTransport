using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WorkItemCardDisplay : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text busIDText;
    public TMP_Text issueTypeText;
    public TMP_Text technicianText;
    public TMP_Text estimatedTimeText;
    public TMP_Text statusText;
    public Image statusBadge;
    public Button prioritizeButton;

    private static readonly Color ColorInRepair        = new Color(0.20f, 0.75f, 0.30f); // green
    private static readonly Color ColorAwaitingParts   = new Color(0.95f, 0.75f, 0.10f); // yellow
    private static readonly Color ColorAwaitingTech    = new Color(0.90f, 0.25f, 0.25f); // red

    private WorkItem _currentItem;
    private Action<WorkItem> _onPrioritize;

    public WorkItem CurrentItem => _currentItem;

    public void Setup(WorkItem item, Action<WorkItem> onPrioritize)
    {
        _currentItem = item;
        _onPrioritize = onPrioritize;

        busIDText.text = item.BusID;
        issueTypeText.text = item.IssuePartType.ToString();
        technicianText.text = string.IsNullOrEmpty(item.AssignedTechnicianName) ? "—" : item.AssignedTechnicianName;
        estimatedTimeText.text = item.EstimatedCompletionLabel;
        statusText.text = GetStatusLabel(item.Status);

        if (statusBadge != null)
            statusBadge.color = GetStatusColor(item.Status);

        prioritizeButton.onClick.RemoveAllListeners();
        prioritizeButton.onClick.AddListener(OnPrioritizePressed);

        // Disable the button if the item is already being repaired
        prioritizeButton.interactable = item.Status != WorkItemStatus.InRepair;
    }

    private void OnPrioritizePressed()
    {
        _onPrioritize?.Invoke(_currentItem);
    }

    private static string GetStatusLabel(WorkItemStatus status)
    {
        switch (status)
        {
            case WorkItemStatus.InRepair:           return "In Repair";
            case WorkItemStatus.AwaitingParts:      return "Awaiting Parts";
            case WorkItemStatus.AwaitingTechnician: return "Awaiting Technician";
            default: return status.ToString();
        }
    }

    private static Color GetStatusColor(WorkItemStatus status)
    {
        switch (status)
        {
            case WorkItemStatus.InRepair:           return ColorInRepair;
            case WorkItemStatus.AwaitingParts:      return ColorAwaitingParts;
            case WorkItemStatus.AwaitingTechnician: return ColorAwaitingTech;
            default: return Color.white;
        }
    }
}
