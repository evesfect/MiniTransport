using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SellBusRequestUI : MonoBehaviour
{
    [Header("UI Scroll Setup")]
    public Transform contentContainer; // The empty RectTransform inside the Scroll View 'Content'
    public SellBusCardUI cardPrefab;

    [Header("General Controls")]
    public TextMeshProUGUI requestOverviewText;
    public Button sendRequestButton;

    private List<SellBusCardUI> _spawnedCards = new List<SellBusCardUI>();
    private List<string> _selectedBusIDs = new List<string>();
    private const int MAX_SELL = 5;

    private void OnEnable()
    {
        // Add listeners
        sendRequestButton.onClick.AddListener(SendSellRequest);

        if (FleetManager.Instance != null)
        {
            // Listen for fleet updates so list stays correct
            FleetManager.Instance.OnFleetUpdated += RefreshBusList;
            RefreshBusList(); // Initial population
        }
        
        UpdateOverviewUI(); 
    }

    private void OnDisable()
    {
        // Remove listeners
        sendRequestButton.onClick.RemoveListener(SendSellRequest);

        if (FleetManager.Instance != null)
        {
            FleetManager.Instance.OnFleetUpdated -= RefreshBusList;
        }
    }

    private void RefreshBusList()
    {
        if (contentContainer == null || cardPrefab == null || FleetManager.Instance == null) return;

        // Clear lists and destroy objects
        _spawnedCards.Clear();
        _selectedBusIDs.Clear();
        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }

        // Iterate all active buses and only create cards for them
        foreach (var bus in FleetManager.Instance.allBuses)
        {
            var card = Instantiate(cardPrefab, contentContainer);
            card.transform.localScale = Vector3.one;
            card.Setup(bus);
            
            // Subscribe to toggle events from this card
            card.OnCardToggled += HandleCardToggle;
            _spawnedCards.Add(card);
        }
        
        UpdateOverviewUI(); // Refresh summary which might clear selections
    }

    private void HandleCardToggle(SellBusCardUI toggledCard)
    {
        if (toggledCard.IsSelected)
        {
            // Try to add ID
            if (_selectedBusIDs.Count < MAX_SELL)
            {
                _selectedBusIDs.Add(toggledCard.BusID);
            }
            else
            {
                // We are already at max, silently force toggle off
                toggledCard.SetToggleState(false, false);
            }
        }
        else
        {
            _selectedBusIDs.Remove(toggledCard.BusID);
        }

        UpdateOverviewUI();
    }

    private void UpdateOverviewUI()
    {
        if (_selectedBusIDs.Count > 0)
        {
            // Payload description list
            string busesString = string.Join(", ", _selectedBusIDs);
            requestOverviewText.text = $"Summary:\nRequesting to sell {_selectedBusIDs.Count} specific buses.\nBus IDs: {busesString}.";
        }
        else
        {
            requestOverviewText.text = "Summary:\nSelect at least one bus from the left scroll view to create a sell request.";
        }
    }

    private void SendSellRequest()
    {
        if (_selectedBusIDs.Count == 0) return;

        int quantity = _selectedBusIDs.Count;
        // Payload is comma-separated list of Bus IDs
        string payloadString = string.Join(",", _selectedBusIDs);

        RequestManager.Instance.CreateRequest(RequestType.SellBus, PlayerRole.FinanceManager, quantity, payloadString);
        
        // Optional clear selection
        foreach(var card in _spawnedCards) card.SetToggleState(false, false);
        _selectedBusIDs.Clear();
        UpdateOverviewUI();
    }
}