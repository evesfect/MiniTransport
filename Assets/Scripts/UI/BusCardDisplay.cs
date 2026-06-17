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

    // Local optimistic flag: true between pressing Recall and the bus actually parking. Used only
    // for this card's status text; the authoritative state lives on the server.
    private bool _recallRequested;

    public void Setup(BusData bus, Action<BusData> onInfoClicked)
    {
        currentBus = bus;
        onInfoClickedCallback = onInfoClicked;
        _recallRequested = false;

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

        // 3. Set TRUE Current Assignment Status. These now read the server-mirrored status list, so
        // they're correct on clients too (not just the host).
        bool isDriving = FleetManager.Instance != null && FleetManager.Instance.IsBusActive(currentBus.BusID);
        bool isBroken = isDriving && FleetManager.Instance.IsBusBroken(currentBus.BusID);
        bool isReturning = isDriving && FleetManager.Instance.IsBusReturning(currentBus.BusID);

        // Clear the local optimistic flag once the server confirms the recall, or the bus parks.
        if (!isDriving || isReturning) _recallRequested = false;

        bool showReturning = isReturning || (_recallRequested && isDriving);

        if (statusText != null)
        {
            if (showReturning)
            {
                statusText.text = isBroken ? "Status: Towing to depot…" : "Status: Returning to depot…";
            }
            else if (isBroken)
            {
                statusText.text = "Status: Broken down (Recall to tow)";
            }
            else if (isDriving)
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

        // Recall only makes sense while the bus is out on the road and not already on its way back.
        if (recallButton != null)
            recallButton.interactable = isDriving && !showReturning;
    }

    private void OnRecallPressed()
    {
        if (currentBus == null || FleetManager.Instance == null) return;

        _recallRequested = true;
        FleetManager.Instance.RecallBusClient(currentBus.BusID);
        Debug.Log($"[BusCardDisplay] Recall requested for bus {currentBus.BusID}.");
    }

    private void OnInfoPressed()
    {
        onInfoClickedCallback?.Invoke(currentBus);
    }
}