using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class MaintenanceQueueCardDisplay : MonoBehaviour
{
    public WorkItem CurrentItem { get; private set; }

    [Header("Text References")]
    public TMP_Text busIdText;
    public TMP_Text statusText;
    public TMP_Text capacityCostText;

    [Header("Buttons")]
    public Button infoButton; // Link your newly renamed 'Info' button here

    private Action<WorkItem> _onPrioritizeCallback;
    private Action<WorkItem> _onInfoCallback; // New callback for the popup

    public void Setup(WorkItem item, Action<WorkItem> onPrioritize, Action<WorkItem> onInfo, float capacityCost)
    {
        CurrentItem = item;
        _onPrioritizeCallback = onPrioritize;
        _onInfoCallback = onInfo;

        if (busIdText != null) busIdText.text = $"Bus: {item.BusID}";
        if (statusText != null) statusText.text = item.EstimatedCompletionLabel;

        if (capacityCostText != null)
        {
            capacityCostText.text = $"Demand: {capacityCost:F0} Cap/hr";
        }

        // Ensure listeners are clean, then add the new one
        if (infoButton != null)
        {
            infoButton.onClick.RemoveAllListeners();
            infoButton.onClick.AddListener(OnInfoButtonClicked);
        }
    }

    public void OnPrioritizeButtonClicked()
    {
        _onPrioritizeCallback?.Invoke(CurrentItem);
    }

    private void OnInfoButtonClicked()
    {
        _onInfoCallback?.Invoke(CurrentItem);
    }
}