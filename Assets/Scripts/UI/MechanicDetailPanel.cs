using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class MechanicDetailPanel : MonoBehaviour
{
    [Header("Detail UI References")]
    [SerializeField] private TMP_Text idText;
    [SerializeField] private TMP_Text specializationText;
    [SerializeField] private TMP_Text skillTierText;
    [SerializeField] private TMP_Text currentAssignmentText;

    [Header("Assignment")]
    [Tooltip("Dropdown listing every depot. Changing it re-lists that depot's teams below.")]
    [SerializeField] private TMP_Dropdown depotDropdown;
    [Tooltip("Dropdown listing the selected depot's teams; the last entry creates a brand-new team.")]
    [SerializeField] private TMP_Dropdown teamDropdown;

    private const string UnassignedLabel = "Unassigned";
    private const string CreateNewTeamLabel = "➕ Create New Team";

    private string _currentEmployeeID;
    private string _mechanicDepot;   // the mechanic's actual assigned depot ("" if none)
    private string _mechanicTeam;    // the mechanic's actual (normalized) team
    private string _currentDepot;    // depot currently selected in the dropdown
    private List<string> _depotOptions = new List<string>();
    private List<string> _teamOptions = new List<string>();
    private Coroutine _refreshRoutine;

    private void Start()
    {
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (EmployeeManager.Instance != null)
            EmployeeManager.Instance.OnEmployeeDataUpdated += RefreshFromData;
    }

    private void OnDisable()
    {
        if (EmployeeManager.Instance != null)
            EmployeeManager.Instance.OnEmployeeDataUpdated -= RefreshFromData;
        if (depotDropdown != null) depotDropdown.onValueChanged.RemoveListener(OnDepotDropdownChanged);
        if (teamDropdown != null) teamDropdown.onValueChanged.RemoveListener(OnTeamDropdownChanged);
    }

    public void PopulateDetailView(EmployeeData data)
    {
        gameObject.SetActive(true);
        _currentEmployeeID = data.EmployeeID;
        _mechanicDepot = data.AssignedDepotID ?? "";
        _mechanicTeam = string.IsNullOrEmpty(data.AssignedTeamID) ? "General Crew" : data.AssignedTeamID;

        idText.text = $"ID: {data.EmployeeID}";
        skillTierText.text = $"Individual Skill: {data.SkillLevel:F0}";

        bool isUnassignedDepot = string.IsNullOrEmpty(data.AssignedDepotID);
        currentAssignmentText.text = $"Depot: {(isUnassignedDepot ? "Unassigned" : data.AssignedDepotID)}";

        if (isUnassignedDepot)
        {
            // No depot yet — the dropdowns below let the player place them on a depot + team.
            specializationText.text = "Team: N/A\nCombined Capacity: N/A (pick a depot & team below to assign)";
        }
        else
        {
            float totalTeamCapacity = 0f;
            int teamMemberCount = 0;

            if (EmployeeManager.Instance != null && EmployeeManager.Instance.allEmployees != null)
            {
                var teamMembers = EmployeeManager.Instance.allEmployees.Where(e =>
                    e.Role == EmployeeRole.Mechanic &&
                    e.AssignedDepotID == data.AssignedDepotID &&
                    (string.IsNullOrEmpty(e.AssignedTeamID) ? "General Crew" : e.AssignedTeamID) == _mechanicTeam
                ).ToList();

                // Mechanics away on a training course don't count toward active team capacity.
                totalTeamCapacity = teamMembers.Where(m => !m.IsInTraining).Sum(m => m.SkillLevel);
                teamMemberCount = teamMembers.Count;
            }

            specializationText.text = $"Team: {_mechanicTeam}\nCombined Capacity: {totalTeamCapacity:F0} / 50 ({teamMemberCount} members)";
        }

        PopulateDepotDropdown();
        PopulateTeamDropdown(_currentDepot, _currentDepot == _mechanicDepot ? _mechanicTeam : null);
    }

    private void PopulateDepotDropdown()
    {
        _depotOptions = GetAllDepotIDs();
        if (_depotOptions.Count == 0)
            _depotOptions.Add(string.IsNullOrEmpty(_mechanicDepot) ? "Depot_Main" : _mechanicDepot);

        // Select the mechanic's depot if known, otherwise the first depot.
        int idx = _depotOptions.IndexOf(_mechanicDepot);
        if (idx < 0) idx = 0;
        _currentDepot = _depotOptions[idx];

        // _currentDepot is resolved above, so the team dropdown still works if the depot dropdown is unwired.
        if (depotDropdown == null) return;

        depotDropdown.onValueChanged.RemoveListener(OnDepotDropdownChanged);
        depotDropdown.ClearOptions();
        depotDropdown.AddOptions(_depotOptions);
        depotDropdown.SetValueWithoutNotify(idx);
        depotDropdown.RefreshShownValue();
        depotDropdown.onValueChanged.AddListener(OnDepotDropdownChanged);
    }

    private void PopulateTeamDropdown(string depotID, string preselectTeam)
    {
        if (teamDropdown == null || EmployeeManager.Instance == null) return;

        _teamOptions = EmployeeManager.Instance.GetTeamsForDepot(depotID);

        teamDropdown.onValueChanged.RemoveListener(OnTeamDropdownChanged);
        teamDropdown.ClearOptions();

        // Options: [Unassigned] + teams + [Create New Team]. Unassigned at index 0 means a new
        // hire defaults to it, so picking a real team is an actual change that fires the callback.
        var options = new List<string> { UnassignedLabel };
        options.AddRange(_teamOptions);
        options.Add(CreateNewTeamLabel);
        teamDropdown.AddOptions(options);

        int teamIdx = preselectTeam != null ? _teamOptions.IndexOf(preselectTeam) : -1;
        teamDropdown.SetValueWithoutNotify(teamIdx >= 0 ? teamIdx + 1 : 0); // +1 for the Unassigned slot
        teamDropdown.RefreshShownValue();

        teamDropdown.onValueChanged.AddListener(OnTeamDropdownChanged);
    }

    // Changing depot just re-lists that depot's teams; the move is committed when a team is picked.
    private void OnDepotDropdownChanged(int index)
    {
        if (index < 0 || index >= _depotOptions.Count) return;
        _currentDepot = _depotOptions[index];
        PopulateTeamDropdown(_currentDepot, _currentDepot == _mechanicDepot ? _mechanicTeam : null);
    }

    private void OnTeamDropdownChanged(int index)
    {
        if (EmployeeManager.Instance == null || string.IsNullOrEmpty(_currentEmployeeID)) return;

        // Layout: 0 = Unassigned, 1.._teamOptions.Count = teams, last = Create New Team.
        if (index == 0)
        {
            EmployeeManager.Instance.UnassignMechanic(_currentEmployeeID);
        }
        else if (index > _teamOptions.Count)
        {
            // Last option: spin up a fresh team (next "Team X") and move this mechanic onto it.
            EmployeeManager.Instance.AssignMechanicToNewTeam(_currentEmployeeID, _currentDepot);
        }
        else
        {
            EmployeeManager.Instance.AssignMechanicToTeam(_currentEmployeeID, _currentDepot, _teamOptions[index - 1]);
        }
    }

    private List<string> GetAllDepotIDs()
    {
        return FindObjectsByType<DepotController>(FindObjectsSortMode.None)
            .Select(d => d.depotID)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    // Re-pull the live employee record after a server sync (e.g. our own assignment) so the panel stays current.
    // Deferred a frame: on the host the sync fires synchronously inside a dropdown's own change callback,
    // so rebuilding the dropdowns immediately would be re-entrant.
    private void RefreshFromData()
    {
        if (!isActiveAndEnabled) return;
        if (_refreshRoutine != null) StopCoroutine(_refreshRoutine);
        _refreshRoutine = StartCoroutine(RefreshNextFrame());
    }

    private IEnumerator RefreshNextFrame()
    {
        yield return null;
        _refreshRoutine = null;

        if (string.IsNullOrEmpty(_currentEmployeeID) || EmployeeManager.Instance?.allEmployees == null) yield break;
        var emp = EmployeeManager.Instance.allEmployees.FirstOrDefault(e => e.EmployeeID == _currentEmployeeID);
        if (emp != null) PopulateDetailView(emp);
    }
}
