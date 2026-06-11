using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MechanicCardDisplay : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text mechanicNameText;
    [SerializeField] private TMP_Text mechanicStatusText; 
    [SerializeField] private Button infoButton;

    private EmployeeData _employeeData;

    public void Populate(EmployeeData data, System.Action<EmployeeData> onInfoClicked)
    {
        _employeeData = data;

        // Use FullName from EmployeeDataTypes.cs
        mechanicNameText.text = data.FullName;
        
        // Status checks if they have a Depot assigned
        if (string.IsNullOrEmpty(data.AssignedDepotID))
        {
            mechanicStatusText.text = "Available";
            mechanicStatusText.color = Color.green;
        }
        else
        {
            mechanicStatusText.text = $"At {data.AssignedDepotID}";
            mechanicStatusText.color = Color.yellow;
        }

        infoButton.onClick.RemoveAllListeners();
        infoButton.onClick.AddListener(() => onInfoClicked?.Invoke(_employeeData));
    }
}