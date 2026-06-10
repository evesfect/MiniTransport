using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HREmployeeRowUI : MonoBehaviour
{
    public TMP_Text nameText;
    public TMP_Text skillText;
    public TMP_Text assignmentText;
    public TMP_Text wageText;
    public TMP_Text trainCostText;

    public Button trainButton;
    public Button fireButton;

    private string _targetEmployeeID;

    public void Setup(EmployeeData data)
    {
        _targetEmployeeID = data.EmployeeID;

        if (nameText != null) nameText.text = data.FullName;
        if (skillText != null) skillText.text = $"Skill: {data.SkillLevel:F0}";
        if (wageText != null) wageText.text = $"Wage: ${data.WeeklySalary:F0}/wk";

        if (assignmentText != null)
        {
            string depot = string.IsNullOrEmpty(data.AssignedDepotID) ? "Idle" : data.AssignedDepotID;
            string team = string.IsNullOrEmpty(data.AssignedTeamID) ? "" : $" ({data.AssignedTeamID})";
            assignmentText.text = $"Depot: {depot}{team}";
        }

        if (trainCostText != null && EmployeeManager.Instance != null)
        {
            float cost = EmployeeManager.Instance.GetTrainingCost(_targetEmployeeID);
            trainCostText.text = data.SkillLevel >= 100f ? "MAX" : $"Train: ${cost:F0}";
        }

        if (trainButton != null)
        {
            trainButton.interactable = data.SkillLevel < 100f;
            trainButton.onClick.RemoveAllListeners();
            trainButton.onClick.AddListener(() => EmployeeManager.Instance.TrainEmployee(_targetEmployeeID));
        }

        if (fireButton != null)
        {
            fireButton.onClick.RemoveAllListeners();
            fireButton.onClick.AddListener(() => EmployeeManager.Instance.FireEmployee(_targetEmployeeID));
        }
    }
}