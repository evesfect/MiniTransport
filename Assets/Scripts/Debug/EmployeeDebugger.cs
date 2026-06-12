using UnityEngine;
using System.Linq;

public class EmployeeDebugger : MonoBehaviour
{
    // Positioned at X=10 to be safe
    private Rect _windowRect = new Rect(10, 20, 360, 600); // Increased height for new UI

    // Default to FALSE (Expanded)
    private bool _isCollapsed = false;

    // Selection state
    private string _selectedId = "";
    private string _targetDepotId = "Depot_Main";
   

    private Vector2 _scrollEmployees;
    private Vector2 _scrollCandidates;

    private const float COLLAPSED_HEIGHT = 60f;
    private const float EXPANDED_HEIGHT = 600f;

    private void OnGUI()
    {
        _windowRect.height = _isCollapsed ? COLLAPSED_HEIGHT : EXPANDED_HEIGHT;

        // Clamp to screen
        _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
        _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);

        _windowRect = GUI.Window(999, _windowRect, DrawWindow, "HR Debugger");
    }

    private void DrawWindow(int id)
    {
        if (EmployeeManager.Instance == null)
        {
            GUILayout.Label("Waiting for EmployeeManager...");
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
            return;
        }

        GUILayout.BeginVertical();
        DrawHeader();

        if (!_isCollapsed)
        {
            DrawStaffList();
            GUILayout.Space(10);
            DrawCandidateList();
            GUILayout.Space(10);
            DrawSelectedEmployee();
        }

        GUILayout.EndVertical();
        // Allow dragging only by the header
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    #region Header
    private void DrawHeader()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);
        string btnText = _isCollapsed ? "[+]" : "[-]";
        if (GUILayout.Button(btnText, GUILayout.Width(30))) _isCollapsed = !_isCollapsed;
        GUILayout.Label("HR Player Actions", GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
    }
    #endregion

    #region Staff List
    private void DrawStaffList()
    {
        GUILayout.Label($"Current Staff ({EmployeeManager.Instance.allEmployees.Count})", GUI.skin.box);
        _scrollEmployees = GUILayout.BeginScrollView(_scrollEmployees, GUILayout.Height(150));

        foreach (var emp in EmployeeManager.Instance.allEmployees)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            Color originalColor = GUI.color;
            if (emp.EmployeeID == _selectedId) GUI.color = Color.green;

            if (GUILayout.Button(emp.FullName, GUILayout.Width(120)))
            {
                _selectedId = emp.EmployeeID;
                // Auto-fill the input box with current assignment if exists
                
                if (emp.Role == EmployeeRole.Mechanic && !string.IsNullOrEmpty(emp.AssignedDepotID))
                    _targetDepotId = emp.AssignedDepotID;
            }
            GUI.color = originalColor;

            GUILayout.Label(emp.Role.ToString().Substring(0, 4), GUILayout.Width(40));

            string assignment = emp.AssignedDepotID;
            if (string.IsNullOrEmpty(assignment)) assignment = "-";
            GUILayout.Label(assignment, GUILayout.Width(70));

            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }
    #endregion

    #region Candidate List
    private void DrawCandidateList()
    {
        GUILayout.Label($"Candidates ({EmployeeManager.Instance.candidates.Count})", GUI.skin.box);
        _scrollCandidates = GUILayout.BeginScrollView(_scrollCandidates, GUILayout.Height(100));

        var candidates = EmployeeManager.Instance.candidates;
        for (int i = 0; i < candidates.Count; i++)
        {
            var cand = candidates[i];
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(cand.FullName, GUILayout.Width(100));
            GUILayout.Label(cand.Role.ToString(), GUILayout.Width(70));
            GUILayout.Label($"${cand.WeeklySalary:F0}", GUILayout.Width(40));
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Hire")) EmployeeManager.Instance.HireCandidate(i);
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }
    #endregion

    #region Selected Employee Actions
    private void DrawSelectedEmployee()
    {
        GUILayout.Label("Selected Employee Actions", GUI.skin.box);

        if (string.IsNullOrEmpty(_selectedId))
        {
            GUILayout.Label("Select an employee above.");
            return;
        }

        var emp = EmployeeManager.Instance.allEmployees.FirstOrDefault(e => e.EmployeeID == _selectedId);
        if (emp == null) return;

        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label($"{emp.FullName} ({emp.Role})");
        GUILayout.Label($"Skill: {emp.SkillLevel:F0} | Wage: ${emp.WeeklySalary:F0}");
        GUILayout.EndVertical();

        GUILayout.Space(5);

        // --- ASSIGNMENT UI ---
        if (emp.Role == EmployeeRole.Mechanic)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Depot:", GUILayout.Width(50));
            _targetDepotId = GUILayout.TextField(_targetDepotId);
            if (GUILayout.Button("Assign", GUILayout.Width(60)))
            {
                EmployeeManager.Instance.AssignMechanicToDepot(emp.EmployeeID, _targetDepotId);
            }
            GUILayout.EndHorizontal();
        }
        
        // ---------------------

        GUILayout.Space(5);
        GUILayout.BeginHorizontal();

        // Train
        if (emp.IsInTraining)
        {
            GUILayout.Label($"Training: {emp.TrainingDaysRemaining}d left");
        }
        else if (emp.SkillLevel < 100)
        {
            // Debug shortcut: enroll in a single day of training.
            float cost = EmployeeManager.Instance.GetTrainingCost(emp.EmployeeID, 1);
            if (GUILayout.Button($"Train 1d (${cost:F0})")) EmployeeManager.Instance.TrainEmployee(emp.EmployeeID, 1);
        }

        // Fire
        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("Fire"))
        {
            EmployeeManager.Instance.FireEmployee(emp.EmployeeID);
            _selectedId = "";
        }
        GUI.backgroundColor = Color.white;

        GUILayout.EndHorizontal();
    }
    #endregion
}