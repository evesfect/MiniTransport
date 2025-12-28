using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using System;


[DefaultExecutionOrder(-50)]
public class EmployeeManager : NetworkBehaviour
{
    public static EmployeeManager Instance { get; private set; }

    [Header("Database")]
    public List<EmployeeData> allEmployees = new List<EmployeeData>();

    [Header("HR - Candidates")]
    public List<EmployeeData> candidates = new List<EmployeeData>();
    public int maxCandidates = 4;
    [Tooltip("Probability (0-100) of a candidate being 'Skilled' vs 'Novice'")]
    public float skilledCandidateChance = 20f;

    [Header("Financial Settings")]
    [Tooltip("Fixed weekly cost per employee (Food, Insurance) regardless of skill.")]
    public float upkeepPerEmployee = 50f;
    [Tooltip("Base salary for a skill level of 0.")]
    public float baseWeeklyWage = 200f;
    [Tooltip("Additional salary per skill point.")]
    public float wagePerSkillPoint = 2f;

    [Header("HR Settings")]
    public float trainingCostBase = 300f;
    public float hiringFee = 100f;

    // Events for UI
    public event Action<string> OnEmployeeHired;
    public event Action<string> OnEmployeeFired;
    public event Action<string, float> OnEmployeeTrained; // ID, NewSkill

#if UNITY_EDITOR
    private string SavePath => Path.Combine(Application.dataPath, "employees.json");
#else
    private string SavePath => Path.Combine(Application.persistentDataPath, "employees.json");
#endif

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            LoadEmployees();
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

            // Listen to the Company Signal (Weekly)
            if (CompanyManager.Instance != null)
            {
                CompanyManager.Instance.OnWeeklyExpensesRequested += SubmitPayroll;
                CompanyManager.Instance.OnWeeklyExpensesRequested += RefreshCandidates;
            }

            // Initial Population if empty
            if (candidates.Count == 0) RefreshCandidates();
        }
        else
        {
            allEmployees.Clear();
            candidates.Clear();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

            if (CompanyManager.Instance != null)
            {
                CompanyManager.Instance.OnWeeklyExpensesRequested -= SubmitPayroll;
                CompanyManager.Instance.OnWeeklyExpensesRequested -= RefreshCandidates;
            }
        }
    }

    // --- Passive Logic: Payroll & Candidates (Triggered by Company Signal) ---

    private void SubmitPayroll()
    {
        if (allEmployees.Count == 0) return;

        float totalSalary = 0f;
        foreach (var emp in allEmployees) totalSalary += emp.WeeklySalary;

        float totalUpkeep = allEmployees.Count * upkeepPerEmployee;

        Debug.Log($"[EmployeeManager] Submitting Payroll: {totalSalary} salary, {totalUpkeep} upkeep");

        if (totalSalary > 0)
        {
            CompanyManager.Instance.ProcessPassiveExpense(
                totalSalary,
                TransactionCategory.StaffSalary,
                $"Weekly Payroll ({allEmployees.Count} staff)"
            );
        }

        if (totalUpkeep > 0)
        {
            CompanyManager.Instance.ProcessPassiveExpense(
                totalUpkeep,
                TransactionCategory.StaffUpkeep,
                $"Staff Upkeep/Benefits"
            );
        }
    }

    private void RefreshCandidates()
    {
        candidates.Clear();
        for (int i = 0; i < maxCandidates; i++)
        {
            // Randomly generate a "Role" (could be weighted)
            EmployeeRole role = (UnityEngine.Random.value > 0.5f) ? EmployeeRole.Driver : EmployeeRole.Mechanic;

            // Randomly generate Skill
            float skill = (UnityEngine.Random.Range(0f, 100f) < skilledCandidateChance)
                ? UnityEngine.Random.Range(40f, 70f) // Experienced
                : UnityEngine.Random.Range(0f, 20f);  // Novice

            float wage = CalculateWageForSkill(skill);

            EmployeeData applicant = new EmployeeData
            {
                EmployeeID = System.Guid.NewGuid().ToString().Substring(0, 8),
                FullName = $"Applicant {UnityEngine.Random.Range(100, 999)}", 
                Role = role,
                SkillLevel = skill,
                WeeklySalary = wage,
                AssignedBusID = ""
            };
            candidates.Add(applicant);
        }

        SyncEmployeesRpc(SerializeEmployees());
        Debug.Log("[EmployeeManager] Candidates Refreshed.");
    }

    // --- HR PLAYER ACTIONS (Public API) ---

    /// <summary>
    /// Hires a specific candidate from the 'candidates' list by index.
    /// </summary>
    public void HireCandidate(int candidateIndex)
    {
        if (IsServer) HireCandidateInternal(candidateIndex);
        else RequestHireCandidateRpc(candidateIndex);
    }

    /// <summary>
    /// Fires an existing employee by ID.
    /// </summary>
    public void FireEmployee(string employeeID)
    {
        if (IsServer) FireInternal(employeeID);
        else RequestFireRpc(employeeID);
    }

    /// <summary>
    /// Trains an employee to increase skill. Costs money.
    /// </summary>
    public void TrainEmployee(string employeeID)
    {
        if (IsServer) TrainInternal(employeeID);
        else RequestTrainRpc(employeeID);
    }

    // --- Helpers ---

    public float GetTrainingCost(string employeeID)
    {
        var emp = allEmployees.FirstOrDefault(e => e.EmployeeID == employeeID);
        if (emp == null) return 0f;
        return trainingCostBase + (emp.SkillLevel * 10f);
    }

    public float CalculateWageForSkill(float skill)
    {
        return baseWeeklyWage + (skill * wagePerSkillPoint);
    }

    // --- Internal Logic (Server) ---

    private void HireCandidateInternal(int index)
    {
        if (index < 0 || index >= candidates.Count) return;

        EmployeeData candidate = candidates[index];

       float cost = hiringFee + CalculateWageForSkill((int)candidate.SkillLevel);

        // 1. Pay Hiring Fee
        bool success = CompanyManager.Instance.TryExecuteActionableTransaction(
            hiringFee,
            TransactionCategory.General,
            $"Hiring Fee: {candidate.FullName}"
        );

        if (success)
        {
            // 2. Move from Candidate list to Employee list
            allEmployees.Add(candidate);
            candidates.RemoveAt(index);

            SaveEmployees();
            SyncEmployeesRpc(SerializeEmployees());
            OnEmployeeHired?.Invoke(candidate.EmployeeID);
            Debug.Log($"[HR] Hired {candidate.FullName}");
        }
    }

    private void FireInternal(string id)
    {
        EmployeeData toRemove = allEmployees.FirstOrDefault(e => e.EmployeeID == id);
        if (toRemove != null)
        {
            allEmployees.Remove(toRemove);
            SaveEmployees();
            SyncEmployeesRpc(SerializeEmployees());
            OnEmployeeFired?.Invoke(id);
            Debug.Log($"[HR] Fired {toRemove.FullName}");
        }
    }

    private void TrainInternal(string id)
    {
        EmployeeData emp = allEmployees.FirstOrDefault(e => e.EmployeeID == id);
        if (emp == null) return;
        if (emp.SkillLevel >= 100f) return; // Maxed out

        float cost = trainingCostBase + (emp.SkillLevel * 10f);

        bool paid = CompanyManager.Instance.TryExecuteActionableTransaction(
            cost,
            TransactionCategory.General,
            $"Training Course for {emp.FullName}"
        );

        if (paid)
        {
            emp.SkillLevel += 5f;
            if (emp.SkillLevel > 100f) emp.SkillLevel = 100f;

            // Raise Salary
            emp.WeeklySalary = CalculateWageForSkill(emp.SkillLevel);

            SaveEmployees();
            SyncEmployeesRpc(SerializeEmployees());
            OnEmployeeTrained?.Invoke(id, emp.SkillLevel);
            Debug.Log($"[HR] Trained {emp.FullName}. New Skill: {emp.SkillLevel}");
        }
    }

    // --- Networking ---

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer) SyncEmployeesRpc(SerializeEmployees(), RpcTarget.Single(clientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.Server)]
    private void RequestHireCandidateRpc(int index) { HireCandidateInternal(index); }

    [Rpc(SendTo.Server)]
    private void RequestFireRpc(string id) { FireInternal(id); }

    [Rpc(SendTo.Server)]
    private void RequestTrainRpc(string id) { TrainInternal(id); }

    [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
    private void SyncEmployeesRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        var container = JsonUtility.FromJson<EmployeeContainer>(json);
        if (container != null)
        {
            allEmployees = container.Employees;
            candidates = container.Candidates;
        }
    }

    // --- Persistence ---

    private string SerializeEmployees()
    {
        return JsonUtility.ToJson(new EmployeeContainer
        {
            Employees = allEmployees,
            Candidates = candidates
        }, true);
    }

    [ContextMenu("Save")]
    public void SaveEmployees()
    {
        File.WriteAllText(SavePath, SerializeEmployees());
    }

    [ContextMenu("Load")]
    public void LoadEmployees()
    {
        if (File.Exists(SavePath))
        {
            var container = JsonUtility.FromJson<EmployeeContainer>(File.ReadAllText(SavePath));
            if (container != null)
            {
                allEmployees = container.Employees;
                candidates = container.Candidates ?? new List<EmployeeData>();
            }
        }
    }
}