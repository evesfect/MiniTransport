using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class BusCardDisplay : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text healthText;
    public TMP_Text statusText;
    public Button recallButton;
    public Button infoButton;

    private BusData currentBus;

    // We can use Actions (delegates) to tell the manager when a button is clicked
    private Action<BusData> onInfoClickedCallback;

    public void Setup(BusData bus, Action<BusData> onInfoClicked)
    {
        currentBus = bus;
        onInfoClickedCallback = onInfoClicked;

        // 1. Set Overall Health
        float avgHealth = bus.GetAverageHealth();
        healthText.text = $"Health: {avgHealth:F1}%";

        // 2. Set Current Assignment Status
        if (bus.Schedule != null && !string.IsNullOrEmpty(bus.Schedule.RouteID))
        {
            statusText.text = $"Status: On Route {bus.Schedule.RouteID}";
        }
        else if (!string.IsNullOrEmpty(bus.AssignedDepotID))
        {
            statusText.text = $"Status: In Depot {bus.AssignedDepotID}";
        }
        else
        {
            statusText.text = "Status: Unassigned";
        }

        // 3. Setup Button Listeners
        // Remove old listeners to prevent multiple fires if the card is reused
        recallButton.onClick.RemoveAllListeners();
        infoButton.onClick.RemoveAllListeners();

        recallButton.onClick.AddListener(OnRecallPressed);
        infoButton.onClick.AddListener(OnInfoPressed);
    }

    private void OnRecallPressed()
    {
        Debug.Log($"Recalling Bus: {currentBus.BusID}");
        // Add your recall logic here, or pass it back via a callback similar to Info
    }

    private void OnInfoPressed()
    {
        // Trigger the callback passed from the manager, sending this specific bus's data
        onInfoClickedCallback?.Invoke(currentBus);
    }
}