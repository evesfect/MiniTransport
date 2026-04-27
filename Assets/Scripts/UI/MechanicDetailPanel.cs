using UnityEngine;
using TMPro;

public class MechanicDetailPanel : MonoBehaviour
{
    [Header("Detail UI References")]
    [SerializeField] private TMP_Text idText;
    [SerializeField] private TMP_Text specializationText;
    [SerializeField] private TMP_Text skillTierText;
    [SerializeField] private TMP_Text currentAssignmentText;

    private void Start()
    {
        gameObject.SetActive(false); // Hide on startup
    }

    public void PopulateDetailView(EmployeeData data)
    {
        gameObject.SetActive(true);

        idText.text = $"ID: {data.EmployeeID}";
        specializationText.text = $"Role: Mechanic"; 
        
        // Formatted to show no decimal places (e.g., 35 instead of 35.0)
        skillTierText.text = $"Skill Level: {data.SkillLevel:F0}"; 

        string assignment = string.IsNullOrEmpty(data.AssignedDepotID) ? "None (Idle)" : data.AssignedDepotID;
        currentAssignmentText.text = $"Assignment: {assignment}";
    }
}