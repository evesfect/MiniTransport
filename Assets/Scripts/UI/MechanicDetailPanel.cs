using UnityEngine;
using TMPro;
using System.Linq;

public class MechanicDetailPanel : MonoBehaviour
{
    [Header("Detail UI References")]
    [SerializeField] private TMP_Text idText;
    [SerializeField] private TMP_Text specializationText; 
    [SerializeField] private TMP_Text skillTierText;
    [SerializeField] private TMP_Text currentAssignmentText;

    private void Start()
    {
        gameObject.SetActive(false);
    }

    public void PopulateDetailView(EmployeeData data)
    {
        gameObject.SetActive(true);

        idText.text = $"ID: {data.EmployeeID}";
        skillTierText.text = $"Individual Skill: {data.SkillLevel:F0}"; 

        bool isUnassignedDepot = string.IsNullOrEmpty(data.AssignedDepotID);
        string depot = isUnassignedDepot ? "Unassigned" : data.AssignedDepotID;
        currentAssignmentText.text = $"Depot: {depot}";

        // If they aren't in a depot, they aren't contributing to any active team capacity
        if (isUnassignedDepot)
        {
            specializationText.text = "Team: N/A\nCombined Capacity: N/A (Assign to a Depot first)";
            return;
        }

        // Normalize empty strings to "General Crew" consistently
        string teamName = string.IsNullOrEmpty(data.AssignedTeamID) ? "General Crew" : data.AssignedTeamID;
        
        float totalTeamCapacity = 0f;
        int teamMemberCount = 0;

        if (EmployeeManager.Instance != null && EmployeeManager.Instance.allEmployees != null)
        {
            // Sum all mechanics sharing this exact Depot and normalized Team Name
            var teamMembers = EmployeeManager.Instance.allEmployees.Where(e => 
                e.Role == EmployeeRole.Mechanic && 
                e.AssignedDepotID == data.AssignedDepotID && 
                (string.IsNullOrEmpty(e.AssignedTeamID) ? "General Crew" : e.AssignedTeamID) == teamName
            ).ToList();

            totalTeamCapacity = teamMembers.Sum(m => m.SkillLevel);
            teamMemberCount = teamMembers.Count;
        }

        specializationText.text = $"Team: {teamName}\nCombined Capacity: {totalTeamCapacity:F0} / 50 ({teamMemberCount} members)";
    }
}