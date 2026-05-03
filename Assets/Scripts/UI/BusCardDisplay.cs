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

    [Header("Card Background")]
    [Tooltip("The Image component that makes up the background of the card")]
    public Image cardBackgroundImage;

    [Header("Health Colors")]
    public Color healthyColor = new Color(0.6f, 1f, 0.6f);  // Soft Green
    public Color warningColor = new Color(1f, 1f, 0.6f);  // Soft Yellow
    public Color criticalColor = new Color(1f, 0.6f, 0.6f); // Soft Red

    [Tooltip("Health percentage below which the card turns yellow")]
    public float warningThreshold = 70f;
    [Tooltip("Health percentage below which the card turns red")]
    public float criticalThreshold = 30f;

    private BusData currentBus;
    private Action<BusData> onInfoClickedCallback;

    public void Setup(BusData bus, Action<BusData> onInfoClicked)
    {
        currentBus = bus;
        onInfoClickedCallback = onInfoClicked;

        // Setup Button Listeners once
        if (recallButton != null)
        {
            recallButton.onClick.RemoveAllListeners();
            recallButton.onClick.AddListener(OnRecallPressed);
        }

        if (infoButton != null)
        {
            infoButton.onClick.RemoveAllListeners();
            infoButton.onClick.AddListener(OnInfoPressed);
        }

        // Force an immediate visual update
        UpdateVisuals();
    }

    // This ensures the card updates in real-time as health decays or status changes!
    private void Update()
    {
        if (currentBus != null)
        {
            UpdateVisuals();
        }
    }

    private void UpdateVisuals()
    {
        // 1. Calculate and Set Live Health
        float avgHealth = currentBus.GetAverageHealth();
        if (healthText != null) healthText.text = $"Health: {avgHealth:F1}%";

        // 2. Change the background color dynamically
        if (cardBackgroundImage != null)
        {
            if (avgHealth < criticalThreshold)
            {
                cardBackgroundImage.color = criticalColor;
            }
            else if (avgHealth < warningThreshold)
            {
                cardBackgroundImage.color = warningColor;
            }
            else
            {
                cardBackgroundImage.color = healthyColor;
            }
        }

        // 3. Set TRUE Current Assignment Status
        if (statusText != null)
        {
            // Check the FleetManager to see if the bus is ACTUALLY spawned and driving
            bool isDriving = FleetManager.Instance != null && FleetManager.Instance.IsBusActive(currentBus.BusID);

            if (isDriving)
            {
                statusText.text = $"Status: Active (Route {currentBus.Schedule.RouteID})";
            }
            else if (!string.IsNullOrEmpty(currentBus.AssignedDepotID))
            {
                statusText.text = $"Status: Parked ({currentBus.AssignedDepotID})";
            }
            else
            {
                statusText.text = "Status: Unassigned";
            }
        }
    }

    private void OnRecallPressed()
    {
        Debug.Log($"Recalling Bus: {currentBus.BusID}");

    }

    private void OnInfoPressed()
    {
        onInfoClickedCallback?.Invoke(currentBus);
    }
}