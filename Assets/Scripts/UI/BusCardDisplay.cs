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

        // 1. Calculate and Set Health
        float avgHealth = bus.GetAverageHealth();
        if (healthText != null) healthText.text = $"Health: {avgHealth:F1}%";

        // 2. Change the background color based on health
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

        // 3. Set Current Assignment Status
        if (statusText != null)
        {
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
        }

        // 4. Setup Button Listeners
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