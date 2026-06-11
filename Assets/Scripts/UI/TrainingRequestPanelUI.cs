using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TrainingRequestPanelUI : MonoBehaviour
{
    [Header("UI Scroll Setup")]
    public Transform contentContainer; // The empty RectTransform inside the Scroll View 'Content'
    public TrainingCardUI cardPrefab;

    [Header("Hiring Request controls (Right Side)")]
    public CanvasGroup hiringGroup; // Use CanvasGroup to easily enable/disable interactivity
    public Slider hireMechanicSlider;
    public TextMeshProUGUI hireMechanicQuantityText;
    public Slider minSkillSlider;
    public TextMeshProUGUI minSkillText;

    [Header("General Controls")]
    public TextMeshProUGUI requestOverviewText;
    public Button sendRequestButton;

    private List<TrainingCardUI> _spawnedCards = new List<TrainingCardUI>();
    private List<string> _selectedMechanicIDs = new List<string>();
    private const int MAX_TRAINING = 5;
    private const int MAX_HIRING = 5;

    private void OnEnable()
    {
        // Add listeners
        hireMechanicSlider.onValueChanged.AddListener(UpdateHiringUI);
        minSkillSlider.onValueChanged.AddListener(UpdateHiringUI);
        sendRequestButton.onClick.AddListener(SendFinalizedRequest);

        if (EmployeeManager.Instance != null)
        {
            EmployeeManager.Instance.OnEmployeeDataUpdated += RefreshMechanicsList;
            RefreshMechanicsList(); // Initial population
        }
        
        // Setup initial UI state
        UpdateHiringUI(hireMechanicSlider.value); 
    }

    private void OnDisable()
    {
        // Remove listeners
        hireMechanicSlider.onValueChanged.RemoveListener(UpdateHiringUI);
        minSkillSlider.onValueChanged.RemoveListener(UpdateHiringUI);
        sendRequestButton.onClick.RemoveListener(SendFinalizedRequest);

        if (EmployeeManager.Instance != null)
        {
            EmployeeManager.Instance.OnEmployeeDataUpdated -= RefreshMechanicsList;
        }
    }

    private void RefreshMechanicsList()
    {
        if (contentContainer == null || cardPrefab == null || EmployeeManager.Instance == null) return;

        // Clear lists and destroy objects
        _spawnedCards.Clear();
        _selectedMechanicIDs.Clear();
        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }

        // Iterate all active employees and only create cards for mechanics
        foreach (var employee in EmployeeManager.Instance.allEmployees)
        {
            if (employee.Role == EmployeeRole.Mechanic)
            {
                var card = Instantiate(cardPrefab, contentContainer);
                card.transform.localScale = Vector3.one;
                card.Setup(employee);
                
                // Subscribe to toggle events from this card
                card.OnCardToggled += HandleCardToggle;
                _spawnedCards.Add(card);
            }
        }
    }

    private void HandleCardToggle(TrainingCardUI toggledCard)
    {
        if (toggledCard.IsSelected)
        {
            // Try to add ID
            if (_selectedMechanicIDs.Count < MAX_TRAINING)
            {
                _selectedMechanicIDs.Add(toggledCard.EmployeeID);
            }
            else
            {
                // We are already at max, silently force toggle off
                toggledCard.SetToggleState(false, false);
            }
        }
        else
        {
            _selectedMechanicIDs.Remove(toggledCard.EmployeeID);
        }

        // Logic check: mutually exclusive requests
        UpdateInteractivity();
    }

    private void UpdateInteractivity()
    {
        // If at least one mechanic selected for training, disable hiring group
        if (_selectedMechanicIDs.Count > 0)
        {
            hiringGroup.interactable = false;
            
            // Set summary for Training request
            requestOverviewText.text = $"Summary:\nRequesting training course for {_selectedMechanicIDs.Count} specified Mechanics.";
        }
        else
        {
            // No mechanics selected, allow hiring
            hiringGroup.interactable = true;
            
            // Re-update hiring UI summary which might have been overridden
            UpdateHiringUI(hireMechanicSlider.value);
        }
    }

    private void UpdateHiringUI(float value)
    {
        // Quantity slider (1-5)
        int hireQuantity = Mathf.RoundToInt(hireMechanicSlider.value);
        hireMechanicQuantityText.text = $" The number of mechanics to be hired: {hireQuantity.ToString()}";

        // Skill level slider (e.g., 0-100)
        float minSkill = minSkillSlider.value;
        minSkillText.text = $"The minimum skill level of mechanics to be hired: {minSkill:F0}";

        // If not in training mode, update overview
        if (_selectedMechanicIDs.Count == 0)
        {
            requestOverviewText.text = $"Summary:\nRequesting to hire {hireQuantity} new mechanics with at least {minSkill:F0} skill level.";
        }
    }

    private void SendFinalizedRequest()
    {
        PlayerRole myRole = RoleManager.Instance.GetMyRole();

        // Check Training Mode
        if (_selectedMechanicIDs.Count > 0)
        {
            // Convert list of selected IDs to comma-separated string payload
            string payloadString = string.Join(",", _selectedMechanicIDs);
            RequestManager.Instance.CreateRequest(RequestType.TrainMechanic, PlayerRole.HRManager, _selectedMechanicIDs.Count, payloadString);
        }
        // Check Hiring Mode (only if training mode is not active)
        else
        {
            int quantity = Mathf.RoundToInt(hireMechanicSlider.value);
            float skill = minSkillSlider.value;
            // Payload is the min skill level requirement
            RequestManager.Instance.CreateRequest(RequestType.HireMechanic, PlayerRole.HRManager, quantity, skill.ToString("F0"));
        }

        // Optionally, close panel or clear selection here
    }
}