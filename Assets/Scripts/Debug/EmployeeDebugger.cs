using UnityEngine;
using System.Linq;

public class EmployeeDebugger : MonoBehaviour
{
    // Positioned at X=10 to be safe
    private Rect _windowRect = new Rect(10, 20, 360, 500);

    // Default to FALSE (Expanded) so you can see the content immediately
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
        // Force the height based on the collapsed state
        _windowRect.height = _isCollapsed ? COLLAPSED_HEIGHT : EXPANDED_HEIGHT;

        // Clamp to screen to prevent losing the window
        _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
        _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);

        _windowRect = GUI.Window(999, _windowRect, DrawWindow, "HR Debugger");
    }

    private void DrawWindow(int id)
    {
        if (EmployeeManager.Instance == null)
        {
            GUILayout.Label("Waiting for EmployeeManager...");
            GUILayout.Label("(Ensure EmployeeManager is in the scene)");

            // Fix 1: Restrict drag to top 20 pixels even in this state
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

        // Fix 2: Only allow dragging by the top 20 pixels (The Header)
        // new Rect(x, y, width, height) -> Width is huge to ensure it covers the whole bar
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    #region Header

    private void DrawHeader()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);

        // CHANGED: Use simple text instead of special arrow characters
        string btnText = _isCollapsed ? "[+]" : "[-]";

        if (GUILayout.Button(btnText, GUILayout.Width(30)))
        {
            _isCollapsed = !_isCollapsed;
            Debug.Log($"[Debugger] Toggled. Collapsed: {_isCollapsed}");

            GUIUtility.ExitGUI();
        }

        GUILayout.Label("HR Player Actions", GUILayout.ExpandWidth(true));

        GUILayout.EndHorizontal();
    }

    #endregion

    #region Staff List

    private void DrawStaffList()
    {
        GUILayout.Label($"Current Staff ({EmployeeManager.Instance.allEmployees.Count})", GUI.skin.box);

        _scrollEmployees = GUILayout.BeginScrollView(_scrollEmployees, GUILayout.Height(150));

        if (EmployeeManager.Instance.allEmployees.Count == 0)
        {
            GUILayout.Label("(No employees)");
        }
        else
        {
            foreach (var emp in EmployeeManager.Instance.allEmployees)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);

                // Highlight Logic
                Color originalColor = GUI.color;
                if (emp.EmployeeID == _selectedId) GUI.color = Color.green;

                if (GUILayout.Button(emp.FullName, GUILayout.Width(120)))
                {
                    _selectedId = emp.EmployeeID;
                }

                GUI.color = originalColor; // Reset color

                GUILayout.Label(emp.Role.ToString(), GUILayout.Width(70));
                GUILayout.Label($"Lvl {emp.SkillLevel:F0}", GUILayout.Width(50));
                GUILayout.Label($"${emp.WeeklySalary:F0}", GUILayout.Width(40));

                GUILayout.EndHorizontal();
            }
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
        if (candidates.Count == 0)
        {
            GUILayout.Label("(No candidates available)");
        }
        else
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                var cand = candidates[i];
                GUILayout.BeginHorizontal(GUI.skin.box);

                GUILayout.Label(cand.FullName, GUILayout.Width(100));
                GUILayout.Label(cand.Role.ToString(), GUILayout.Width(70));
                GUILayout.Label($"Lvl {cand.SkillLevel:F0}", GUILayout.Width(45));
                GUILayout.Label($"${cand.WeeklySalary:F0}", GUILayout.Width(40));

                GUI.backgroundColor = Color.green;
                // Important: Passing index 'i' to hire the specific candidate
                if (GUILayout.Button("Hire"))
                {
                    EmployeeManager.Instance.HireCandidate(i);

                    GUIUtility.ExitGUI();
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();
            }
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
            GUILayout.Label("Select an employee from the list above.");
            return;
        }

        var emp = EmployeeManager.Instance.allEmployees.FirstOrDefault(e => e.EmployeeID == _selectedId);
        if (emp == null)
        {
            GUILayout.Label("Employee no longer exists.");
            _selectedId = ""; // Reset invalid ID
            return;
        }

        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label($"Name: {emp.FullName}");
        GUILayout.Label($"Role: {emp.Role}");
        GUILayout.Label($"Skill: {emp.SkillLevel:F1} / 100");
        GUILayout.Label($"Salary: ${emp.WeeklySalary:F2}/week");
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
        else if (emp.Role == EmployeeRole.Driver)
        {
            GUILayout.Label("Driver assignment handled via Fleet Manager.");
        }

        GUILayout.Space(5);
        GUILayout.BeginHorizontal();

        // Train Button
        if (emp.SkillLevel < 100)
        {
            float trainingCost = EmployeeManager.Instance.GetTrainingCost(emp.EmployeeID);
            if (GUILayout.Button($"Train (${trainingCost:F0})"))
            {
                EmployeeManager.Instance.TrainEmployee(emp.EmployeeID);
            }
        }
        else
        {
            GUI.enabled = false;
            GUILayout.Button("Max Skill Reached");
            GUI.enabled = true;
        }

        // Fire Button
        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("Fire Employee"))
        {
            EmployeeManager.Instance.FireEmployee(emp.EmployeeID);
            _selectedId = "";
            GUIUtility.ExitGUI();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.EndHorizontal();
    }

    #endregion
}