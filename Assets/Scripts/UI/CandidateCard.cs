using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HRCandidateCardUI : MonoBehaviour
{
    public TMP_Text nameText;
    public TMP_Text skillText;
    public TMP_Text requestedWageText;
    public TMP_Text hiringFeeText;

    public Button hireButton;

    public void Setup(EmployeeData data, int candidateIndex)
    {
        if (nameText != null) nameText.text = data.FullName;
        float minEst = Mathf.Clamp(data.SkillLevel - 10f, 0f, 100f);
        float maxEst = Mathf.Clamp(data.SkillLevel + 10f, 0f, 100f);

        if (skillText != null) skillText.text = $"Est. Skill: {minEst:F0} - {maxEst:F0}";
        if (requestedWageText != null) requestedWageText.text = $"Demands: ${data.WeeklySalary:F0}/wk";

        if (hiringFeeText != null && EmployeeManager.Instance != null)
        {
            float totalHireCost = EmployeeManager.Instance.hiringFee + data.WeeklySalary;
            hiringFeeText.text = $"Onboard Fee: ${totalHireCost:F0}";
        }

        if (hireButton != null)
        {
            hireButton.onClick.RemoveAllListeners();
            hireButton.onClick.AddListener(() =>
            {
                if (EmployeeManager.Instance != null)
                {
                    EmployeeManager.Instance.HireCandidate(candidateIndex);
                }
            });
        }
    }
}