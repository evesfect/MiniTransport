using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;


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

        // Fallback for scenes where this NetworkBehaviour is not network-spawned.
        // In that case OnNetworkSpawn won't run, so load persisted data here.
        if (allEmployees == null || allEmployees.Count <= 1)
        {
            LoadEmployees();
            if (candidates == null) candidates = new List<EmployeeData>();
        }
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

            if (FleetManager.Instance != null)
            {
                FleetManager.Instance.OnFleetUpdated += AutoAssignDrivers;
            }

            // Initial Population if empty
            if (candidates.Count == 0) RefreshCandidates();

            AutoAssignMechanicsToTeams();
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

    private void AutoAssignDrivers()
    {
        if (!IsServer || FleetManager.Instance == null) return;

        var buses = FleetManager.Instance.allBuses;
        var drivers = allEmployees.Where(e => e.Role == EmployeeRole.Driver).ToList();
        bool changed = false;

        // 1. Cleanup: Unassign drivers if their bus no longer exists
        foreach (var driver in drivers)
        {
            if (!string.IsNullOrEmpty(driver.AssignedBusID))
            {
                if (!buses.Any(b => b.BusID == driver.AssignedBusID))
                {
                    driver.AssignedBusID = ""; // Bus was deleted, driver is now free
                    changed = true;
                }
            }
        }

        // 2. Assignment: Find buses without drivers and assign free drivers
        foreach (var bus in buses)
        {
            // Is this bus already covered by someone?
            bool hasDriver = drivers.Any(d => d.AssignedBusID == bus.BusID);

            if (!hasDriver)
            {
                // Find a driver who is NOT assigned
                var freeDriver = drivers.FirstOrDefault(d => string.IsNullOrEmpty(d.AssignedBusID));

                if (freeDriver != null)
                {
                    freeDriver.AssignedBusID = bus.BusID;
                    changed = true;
                    Debug.Log($"[Auto-Assign] Driver {freeDriver.FullName} assigned to {bus.BusID}");
                }
            }
        }

        if (changed)
        {
            SaveEmployees();
            SyncEmployeesRpc(SerializeEmployees());
        }
    }

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

    /// <summary>
    /// Assigns a Mechanic to a specific Depot ID.
    /// </summary>
    public void AssignMechanicToDepot(string employeeID, string depotID)
    {
        if (IsServer)
        {
            AssignMechanicInternal(employeeID, depotID);
        }
        else
        {
            // If client, request server to do it
            RequestDepotAssignmentRpc(employeeID, depotID);
        }
    }

    //Assigns a Driver to a Bus
    public void AssignDriverToBus(string employeeID, string busID)
    {
        if (IsServer) AssignDriverInternal(employeeID, busID);
        else RequestDriverAssignmentRpc(employeeID, busID);
    }

    // [NEW] Checks if a bus has a driver
    public bool HasAssignedDriver(string busID)
    {
        // Look for any employee who is a Driver AND is assigned to this busID
        return allEmployees.Any(e => e.Role == EmployeeRole.Driver && e.AssignedBusID == busID);
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

    public void AutoAssignMechanicsToTeams()
    {
        if (!IsServer) return;

        bool changed = false;
        float targetCapacity = 50f;

        string[] teamNames = new[] { "Team Alpha", "Team Beta", "Team Gamma", "Team Delta", "Team Echo", "Team Omega" };

        // Group active mechanics by depot
        var depotGroups = allEmployees
            .Where(e => e.Role == EmployeeRole.Mechanic && !string.IsNullOrEmpty(e.AssignedDepotID))
            .GroupBy(e => e.AssignedDepotID);

        foreach (var group in depotGroups)
        {
            var mechanicsInDepot = group.ToList();

            // Check if this depot already has custom teams assigned from a saved game
            bool hasCustomTeams = mechanicsInDepot.Any(m =>
                !string.IsNullOrEmpty(m.AssignedTeamID) &&
                m.AssignedTeamID != "General Crew"
            );

            // If custom teams already exist, leave this depot intact so we don't destroy manual edits
            if (hasCustomTeams) continue;

            // Sort descending by skill level to pack larger skill pools first
            var sortedMechanics = mechanicsInDepot.OrderByDescending(e => e.SkillLevel).ToList();
            List<List<EmployeeData>> teams = new List<List<EmployeeData>>();

            foreach (var mechanic in sortedMechanics)
            {
                var targetTeam = teams.FirstOrDefault(t => t.Sum(m => m.SkillLevel) < targetCapacity);

                if (targetTeam != null)
                {
                    targetTeam.Add(mechanic);
                }
                else
                {
                    teams.Add(new List<EmployeeData> { mechanic });
                }
            }

            // Safety merge: if the final formed team failed to reach 50 capacity, merge it backward
            if (teams.Count > 1)
            {
                var lastTeam = teams.Last();
                if (lastTeam.Sum(m => m.SkillLevel) < targetCapacity)
                {
                    teams.RemoveAt(teams.Count - 1);
                    teams.Last().AddRange(lastTeam);
                }
            }

            // Apply assigned team names back to the serialized data
            for (int i = 0; i < teams.Count; i++)
            {
                string teamName = i < teamNames.Length ? teamNames[i] : $"Team {i + 1}";
                foreach (var mechanic in teams[i])
                {
                    if (mechanic.AssignedTeamID != teamName)
                    {
                        mechanic.AssignedTeamID = teamName;
                        changed = true;
                    }
                }
            }
        }

        if (changed)
        {
            SaveEmployees();
            SyncEmployeesRpc(SerializeEmployees());
            Debug.Log("[Auto-Assign] Initialized mechanic teams ensuring minimum engine repair capacity.");
        }
    }

    public void AssignMechanicToTeam(string employeeID, string depotID, string teamID)
    {
        if (IsServer) AssignMechanicToTeamInternal(employeeID, depotID, teamID);
        else RequestTeamAssignmentRpc(employeeID, depotID, teamID);
    }

    private void AssignMechanicToTeamInternal(string employeeID, string depotID, string teamID)
    {
        var emp = allEmployees.FirstOrDefault(e => e.EmployeeID == employeeID);
        if (emp == null || emp.Role != EmployeeRole.Mechanic) return;

        emp.AssignedDepotID = depotID;
        emp.AssignedTeamID = string.IsNullOrEmpty(teamID) ? "General Crew" : teamID;

        SaveEmployees();
        SyncEmployeesRpc(SerializeEmployees());
        Debug.Log($"[Employee] Assigned {emp.FullName} to Depot: {depotID}, Team: {emp.AssignedTeamID}");
    }

    // --- Internal Logic (Server) ---

    private void HireCandidateInternal(int index)
    {
        if (index < 0 || index >= candidates.Count) return;

        EmployeeData candidate = candidates[index];

       float cost = hiringFee + CalculateWageForSkill((int)candidate.SkillLevel);

        // 1. Pay Hiring Fee
        bool success = CompanyManager.Instance.TryExecuteActionableTransaction(
            cost,
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

            AutoAssignDrivers();
        }
    }

    private void AssignMechanicInternal(string employeeID, string depotID)
    {
        var emp = allEmployees.FirstOrDefault(e => e.EmployeeID == employeeID);
        if (emp == null) return;

        if (emp.Role != EmployeeRole.Mechanic)
        {
            Debug.LogWarning($"[Employee] Cannot assign {emp.Role} to a Depot. Only Mechanics allowed.");
            return;
        }

        emp.AssignedDepotID = depotID;

        // Save and Sync
        SaveEmployees();
        SyncEmployeesRpc(SerializeEmployees());
        Debug.Log($"[Employee] Assigned {emp.FullName} to Depot: {depotID}");
    }

    private void AssignDriverInternal(string employeeID, string busID)
    {
        var emp = allEmployees.FirstOrDefault(e => e.EmployeeID == employeeID);
        if (emp == null || emp.Role != EmployeeRole.Driver) return;

       
        emp.AssignedBusID = busID;

        SaveEmployees();
        SyncEmployeesRpc(SerializeEmployees());
        Debug.Log($"[Employee] Driver {emp.FullName} assigned to Bus {busID}");
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

            AutoAssignDrivers();
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

    [Rpc(SendTo.Server)]
    private void RequestDepotAssignmentRpc(string employeeID, string depotID)
    {
        AssignMechanicInternal(employeeID, depotID);
    }

    [Rpc(SendTo.Server)] 
    private void RequestDriverAssignmentRpc(string eId, string bId) 
    { 
        AssignDriverInternal(eId, bId); 
    }

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

    [Rpc(SendTo.Server)]
    private void RequestTeamAssignmentRpc(string employeeID, string depotID, string teamID)
    {
        AssignMechanicToTeamInternal(employeeID, depotID, teamID);
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
                allEmployees = container.Employees ?? new List<EmployeeData>();
                candidates = container.Candidates ?? new List<EmployeeData>();
            }
        }
        else
        {
            if (allEmployees == null) allEmployees = new List<EmployeeData>();
            if (candidates == null) candidates = new List<EmployeeData>();
            Debug.LogWarning($"[EmployeeManager] employees.json not found at: {SavePath}");
        }

        Debug.Log($"[EmployeeManager] Loaded {allEmployees.Count} employees from: {SavePath}");
    }

    public EmployeeData GetDriverForBus(string busID)
    {
        // Search all employees for one assigned to this bus
        return allEmployees.FirstOrDefault(e => e.AssignedBusID == busID);
    }
}