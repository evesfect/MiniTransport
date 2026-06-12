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

    [Header("Training")]
    [Tooltip("Slider used by HR to pick the number of training days (1..7).")]
    public Slider trainingDaysSlider;
    [Tooltip("Optional label showing the selected number of training days.")]
    public TMP_Text trainingDaysText;

    public Button trainButton;
    public Button fireButton;

    private string _targetEmployeeID;
    private bool _isInTraining;
    private bool _maxed;

    public void Setup(EmployeeData data)
    {
        _targetEmployeeID = data.EmployeeID;
        _isInTraining = data.IsInTraining;
        _maxed = data.SkillLevel >= 100f;

        if (nameText != null) nameText.text = data.FullName;
        if (skillText != null) skillText.text = $"Skill: {data.SkillLevel:F0}";
        if (wageText != null) wageText.text = $"Wage: ${data.WeeklySalary:F0}/wk";

        if (assignmentText != null)
        {
            if (_isInTraining)
            {
                assignmentText.text = $"In Training ({data.TrainingDaysRemaining}d left)";
            }
            else
            {
                string depot = string.IsNullOrEmpty(data.AssignedDepotID) ? "Idle" : data.AssignedDepotID;
                string team = string.IsNullOrEmpty(data.AssignedTeamID) ? "" : $" ({data.AssignedTeamID})";
                assignmentText.text = $"Depot: {depot}{team}";
            }
        }

        bool canTrain = !_maxed && !_isInTraining;

        // Configure the days slider (1..maxTrainingDays whole numbers).
        if (trainingDaysSlider != null)
        {
            int maxDays = EmployeeManager.Instance != null ? EmployeeManager.Instance.maxTrainingDays : 7;

            trainingDaysSlider.onValueChanged.RemoveAllListeners();
            trainingDaysSlider.wholeNumbers = true;
            trainingDaysSlider.minValue = 1;
            trainingDaysSlider.maxValue = Mathf.Max(1, maxDays);
            trainingDaysSlider.SetValueWithoutNotify(1);
            trainingDaysSlider.interactable = canTrain;
            trainingDaysSlider.onValueChanged.AddListener(_ => UpdateTrainingDisplay());
        }

        UpdateTrainingDisplay();

        if (trainButton != null)
        {
            trainButton.interactable = canTrain;
            trainButton.onClick.RemoveAllListeners();
            trainButton.onClick.AddListener(() =>
            {
                int days = SelectedDays();
                EmployeeManager.Instance.TrainEmployee(_targetEmployeeID, days);
            });
        }

        if (fireButton != null)
        {
            fireButton.onClick.RemoveAllListeners();
            fireButton.onClick.AddListener(() => EmployeeManager.Instance.FireEmployee(_targetEmployeeID));
        }
    }

    private int SelectedDays()
    {
        if (trainingDaysSlider == null) return 1;
        return Mathf.Clamp(Mathf.RoundToInt(trainingDaysSlider.value), 1, (int)trainingDaysSlider.maxValue);
    }

    // Refreshes the days label and the quoted training cost for the currently selected number of days.
    private void UpdateTrainingDisplay()
    {
        int days = SelectedDays();

        if (trainingDaysText != null)
            trainingDaysText.text = $"{days} day{(days == 1 ? "" : "s")}";

        if (trainCostText == null || EmployeeManager.Instance == null) return;

        if (_maxed)
        {
            trainCostText.text = "MAX";
        }
        else if (_isInTraining)
        {
            trainCostText.text = "Training...";
        }
        else
        {
            float cost = EmployeeManager.Instance.GetTrainingCost(_targetEmployeeID, days);
            trainCostText.text = $"Train: ${cost:F0}";
        }
    }
}
