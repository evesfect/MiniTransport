using UnityEngine;
using TMPro;
using System;

public class MaintenanceQueueCardDisplay : MonoBehaviour
{
    public WorkItem CurrentItem { get; private set; }

    [Header("Text References")]
    public TMP_Text busIdText;
    public TMP_Text statusText;
    public TMP_Text capacityCostText; // The new field for Panel 3

    // TODO: Add your 5 mini health bar Image references here, 
    // just like you had in the original WorkItemCardDisplay.

    private Action<WorkItem> _onPrioritizeCallback;

    public void Setup(WorkItem item, Action<WorkItem> onPrioritize, float capacityCost)
    {
        CurrentItem = item;
        _onPrioritizeCallback = onPrioritize;

        if (busIdText != null) busIdText.text = $"Bus: {item.BusID}";
        if (statusText != null) statusText.text = item.EstimatedCompletionLabel;

        // Display the demand this repair places on the depot
        if (capacityCostText != null)
        {
            capacityCostText.text = $"Demand: {capacityCost:F0} Cap/hr";
        }

        // TODO: Update your mini health bars based on the item's part type here
    }

    // If you have a specific button to jump a task to the front of the queue, link its OnClick here
    public void OnPrioritizeButtonClicked()
    {
        _onPrioritizeCallback?.Invoke(CurrentItem);
    }
}