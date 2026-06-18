using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TrainingCardUI : MonoBehaviour
{
    [Header("Text Fields")]
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI idText;
    public TextMeshProUGUI assignmentText;
    public TextMeshProUGUI skillText;

    [Header("Controls")]
    public Toggle selectToggle;

    // We store the ID to easily retrieve it later
    public string EmployeeID { get; private set; }
    
    // Parent panel will subscribe to this to listen for toggle changes
    public event Action<TrainingCardUI> OnCardToggled;

    private void Start()
    {
        // Listen for internal toggle changes and pass this card to parent
        selectToggle.onValueChanged.AddListener((isOn) => OnCardToggled?.Invoke(this));
    }

    public void Setup(EmployeeData employee)
    {
        EmployeeID = employee.EmployeeID;

        nameText.text = employee.FullName;
        idText.text = $"ID: {employee.EmployeeID}";
        assignmentText.text = $"Assignment: {employee.AssignedDepotID}";
        skillText.text = $"Skill: {employee.SkillLevel:F0}";

        // Reset toggle when spawned
        selectToggle.isOn = false;
    }

    // Force toggle state (used for limiting selection)
    public void SetToggleState(bool state, bool sendEvent = true)
    {
        if (sendEvent)
        {
            selectToggle.isOn = state;
        }
        else
        {
            // Silently change without invoking listener
            selectToggle.SetIsOnWithoutNotify(state);
        }
    }

    public bool IsSelected => selectToggle.isOn;
}