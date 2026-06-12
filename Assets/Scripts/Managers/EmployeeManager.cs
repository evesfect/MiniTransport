using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public enum AdTier
{
    None,
    Flyers,         // Low cost, mostly novices
    Classifieds,    // Medium cost, solid average mechanics
    Headhunter      // High cost, guaranteed experts
}


[DefaultExecutionOrder(-50)]
public class EmployeeManager : NetworkBehaviour
{
    public static EmployeeManager Instance { get; private set; }

    [Header("Database")]
    public List<EmployeeData> allEmployees = new List<EmployeeData>();

    [Header("HR - Candidates")]
    public List<EmployeeData> candidates = new List<EmployeeData>();
    public int maxCandidates = 4;

    [Header("Recruitment Campaigns (Ad Tiers)")]
    public float costFlyers = 150f;
    public float costClassifieds = 500f;
    public float costHeadhunter = 1200f;

    private AdTier _pendingAdTier = AdTier.None;
    private bool _adCampaignActive = false;

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

    [Header("Training Settings")]
    [Tooltip("Skill points an employee gains for each day spent in training.")]
    public float trainingSkillPerDay = 2f;
    [Tooltip("Maximum length of a single training course, in days.")]
    public int maxTrainingDays = 7;

    // Events for UI
    public event Action<string> OnEmployeeHired;
    public event Action<string> OnEmployeeFired;
    public event Action<string, float> OnEmployeeTrained; // ID, NewSkill
    public event Action OnCandidatesUpdated;
    // [NEW] Event to tell UI panels (like the HR Request Panel) to refresh their lists
    public event Action OnEmployeeDataUpdated;

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
                
            }

            if (SimulationTimeManager.Instance != null)
            {
                SimulationTimeManager.Instance.OnDayChanged += ProcessPendingAdCampaign;
                SimulationTimeManager.Instance.OnDayChanged += ProcessTraining;
            }



            // Initial Population if empty
            if (candidates.Count == 0) DeliverCandidates(AdTier.Flyers);

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
                
            }
            if (SimulationTimeManager.Instance != null)
            {
                SimulationTimeManager.Instance.OnDayChanged -= ProcessPendingAdCampaign;
                SimulationTimeManager.Instance.OnDayChanged -= ProcessTraining;
            }
        }
    }
    // --- ADVERTISEMENT CAMPAIGNS (Player API) ---
    public bool IsCampaignActive => _adCampaignActive;
    public AdTier CurrentCampaignTier => _pendingAdTier;

    public void LaunchAdCampaign(AdTier tier)
    {
        if (IsServer) LaunchAdCampaignInternal(tier);
        else RequestLaunchAdCampaignRpc(tier);
    }

    private void LaunchAdCampaignInternal(AdTier tier)
    {
        if (_adCampaignActive)
        {
            Debug.LogWarning("[HR] An ad campaign is already active and waiting for tomorrow morning.");
            return;
        }

        float cost = tier switch
        {
            AdTier.Flyers => costFlyers,
            AdTier.Classifieds => costClassifieds,
            AdTier.Headhunter => costHeadhunter,
            _ => 0f
        };

        bool success = CompanyManager.Instance.TryExecuteActionableTransaction(
            cost,
            TransactionCategory.General,
            $"Recruitment Campaign: {tier}"
        );

        if (success)
        {
            _pendingAdTier = tier;
            _adCampaignActive = true;
            Debug.Log($"[HR] Successfully launched {tier} ad campaign. Applicants arriving tomorrow.");
            // Push the new campaign state to clients so their HR banner updates immediately.
            SyncEmployeesRpc(SerializeEmployees());
            OnCandidatesUpdated?.Invoke();
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestLaunchAdCampaignRpc(AdTier tier)
    {
        LaunchAdCampaignInternal(tier);
    }

    private void ProcessPendingAdCampaign()
    {
        if (!_adCampaignActive || _pendingAdTier == AdTier.None) return;

        // Clear the campaign state BEFORE delivering, so the snapshot DeliverCandidates
        // syncs to clients already reflects the campaign as resolved (banner clears).
        AdTier resolvedTier = _pendingAdTier;
        _adCampaignActive = false;
        _pendingAdTier = AdTier.None;

        DeliverCandidates(resolvedTier);

        Debug.Log("[HR] Morning arrival: Ad campaign applicants have entered the lobby!");
        OnCandidatesUpdated?.Invoke();
    }

    private void DeliverCandidates(AdTier tier)
    {
        candidates.Clear();

        int applicantsCount = UnityEngine.Random.Range(0, maxCandidates + 1);

        if (applicantsCount == 0)
        {
            Debug.Log("[HR] Bad luck! No applicants showed up for the interview today.");
        }

        for (int i = 0; i < applicantsCount; i++)
        {
            float skill = 0f;

            switch (tier)
            {
                case AdTier.Flyers:
                    // 90% novice (5-25), 10% lucky break (30-50)
                    skill = UnityEngine.Random.Range(0f, 100f) < 10f
                        ? UnityEngine.Random.Range(30f, 50f)
                        : UnityEngine.Random.Range(5f, 25f);
                    break;

                case AdTier.Classifieds:
                    // Solid mid-tier pool (35-65) with some junior mechanics
                    skill = UnityEngine.Random.Range(0f, 100f) < 65f
                        ? UnityEngine.Random.Range(35f, 65f)
                        : UnityEngine.Random.Range(15f, 35f);
                    break;

                case AdTier.Headhunter:
                    // Premium expert pool guaranteed (65-90)
                    skill = UnityEngine.Random.Range(65f, 90f);
                    break;
            }

            float wage = CalculateWageForSkill(skill);

            EmployeeData applicant = new EmployeeData
            {
                EmployeeID = System.Guid.NewGuid().ToString().Substring(0, 8),
                FullName = $"Applicant {UnityEngine.Random.Range(100, 999)}",
                Role = EmployeeRole.Mechanic,
                SkillLevel = skill,
                WeeklySalary = wage,
                AssignedBusID = ""
            };

            candidates.Add(applicant);
        }

        SaveEmployees();
        SyncEmployeesRpc(SerializeEmployees());
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
    /// Enrolls an employee in a training course lasting <paramref name="days"/> days (1..maxTrainingDays).
    /// The company pays the full cost upfront; the employee is away (not working) until the course ends,
    /// gaining skill on each day that passes.
    /// </summary>
    public void TrainEmployee(string employeeID, int days)
    {
        if (IsServer) TrainInternal(employeeID, days);
        else RequestTrainRpc(employeeID, days);
    }

    /// <summary>
    /// Assigns a Mechanic to a specific Depot ID.
    /// </summary>
    public void AssignMechanicToDepot(string employeeID, string depotID)
    {
        if (IsServer) AssignMechanicInternal(employeeID, depotID);
        else RequestDepotAssignmentRpc(employeeID, depotID);
    }

    
    // --- Helpers ---

    /// <summary>
    /// Cost of a single day of training for this employee (scales with their current skill).
    /// </summary>
    public float GetTrainingCost(string employeeID)
    {
        var emp = allEmployees.FirstOrDefault(e => e.EmployeeID == employeeID);
        if (emp == null) return 0f;
        return trainingCostBase + (emp.SkillLevel * 100f);
    }

    /// <summary>
    /// Total cost of a <paramref name="days"/>-day training course (linear: per-day cost x days).
    /// </summary>
    public float GetTrainingCost(string employeeID, int days)
    {
        days = Mathf.Clamp(days, 1, maxTrainingDays);
        return GetTrainingCost(employeeID) * days;
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

        string[] teamNames = new[] { "Team A", "Team B", "Team C", "Team D", "Team E", "Team F" };
        int maxTeamsPerDepot = 3;

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
                    if (teams.Count < maxTeamsPerDepot)
                    {
                        teams.Add(new List<EmployeeData> { mechanic });
                    }
                    else
                    {
                        // 3. We reached the maximum team limit. Distribute remaining mechanics 
                        // to the existing team with the lowest total pooled skill to keep workloads balanced.
                        var lowestTeam = teams.OrderBy(t => t.Sum(m => m.SkillLevel)).First();
                        lowestTeam.Add(mechanic);
                    }
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

        // Charge the candidate's actual demanded wage (what the HR card displays),
        // not a recomputed/truncated value, so the quote and the charge always match.
        float cost = hiringFee + candidate.WeeklySalary;

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

            

            // NEW: Tell Request Manager a mechanic was hired, pass their skill level
            if (candidate.Role == EmployeeRole.Mechanic && RequestManager.Instance != null)
            {
                RequestManager.Instance.NotifyActionTaken(RequestType.HireMechanic, 1, candidate.SkillLevel.ToString());
            }
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

    private void TrainInternal(string id, int days)
    {
        EmployeeData emp = allEmployees.FirstOrDefault(e => e.EmployeeID == id);
        if (emp == null) return;
        if (emp.SkillLevel >= 100f) return;     // Maxed out
        if (emp.IsInTraining) return;           // Already away on a course

        days = Mathf.Clamp(days, 1, maxTrainingDays);

        // Charge the full course upfront. Per-day cost x days (matches the UI quote).
        float cost = GetTrainingCost(id, days);

        bool paid = CompanyManager.Instance.TryExecuteActionableTransaction(
            cost,
            TransactionCategory.General,
            $"Training Course for {emp.FullName} ({days} day{(days == 1 ? "" : "s")})"
        );

        if (paid)
        {
            // Enroll: skill is gained gradually as the days tick down (see ProcessTraining).
            emp.TrainingDaysRemaining = days;

            SaveEmployees();
            SyncEmployeesRpc(SerializeEmployees()); // fires OnEmployeeDataUpdated on host + clients
            Debug.Log($"[HR] Enrolled {emp.FullName} in a {days}-day training course. Away until it completes.");
        }
    }

    /// <summary>
    /// Server-side day tick: advances every active training course. Each enrolled employee gains
    /// <see cref="trainingSkillPerDay"/> skill per day and returns to work when their course ends.
    /// </summary>
    private void ProcessTraining()
    {
        if (!IsServer) return;

        bool changed = false;

        foreach (var emp in allEmployees)
        {
            if (!emp.IsInTraining) continue;

            emp.SkillLevel = Mathf.Min(100f, emp.SkillLevel + trainingSkillPerDay);
            emp.WeeklySalary = CalculateWageForSkill(emp.SkillLevel);
            emp.TrainingDaysRemaining--;
            changed = true;

            if (!emp.IsInTraining)
            {
                Debug.Log($"[HR] {emp.FullName} finished training. New Skill: {emp.SkillLevel}");
                OnEmployeeTrained?.Invoke(emp.EmployeeID, emp.SkillLevel);

                if (emp.Role == EmployeeRole.Mechanic && RequestManager.Instance != null)
                {
                    RequestManager.Instance.NotifyActionTaken(RequestType.TrainMechanic, 1, emp.EmployeeID);
                }
            }
        }

        if (changed)
        {
            SaveEmployees();
            SyncEmployeesRpc(SerializeEmployees()); // fires OnEmployeeDataUpdated on host + clients
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
    private void RequestTrainRpc(string id, int days) { TrainInternal(id, days); }

    [Rpc(SendTo.Server)]
    private void RequestDepotAssignmentRpc(string employeeID, string depotID)
    {
        AssignMechanicInternal(employeeID, depotID);
    }


    [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
    private void SyncEmployeesRpc(string json, RpcParams rpcParams = default)
    {
        // Host already holds the authoritative lists; clients apply the synced snapshot.
        if (!IsServer)
        {
            var container = JsonUtility.FromJson<EmployeeContainer>(json);
            if (container != null)
            {
                allEmployees = container.Employees;
                candidates = container.Candidates;
                _adCampaignActive = container.AdCampaignActive;
                _pendingAdTier = container.PendingAdTier;
            }
        }

        // Notify UI on BOTH host and clients that employee data changed, so panels
        // refresh after a server-side hire/fire/train (clients only learn via this sync).
        OnEmployeeDataUpdated?.Invoke();
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
            Candidates = candidates,
            AdCampaignActive = _adCampaignActive,
            PendingAdTier = _pendingAdTier
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
        // [NEW] Fire UI update event after initial file load
        OnEmployeeDataUpdated?.Invoke();
    }

}