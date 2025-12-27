using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using Unity.Netcode;

[DefaultExecutionOrder(-55)] 
public class CompanyManager : NetworkBehaviour
{
    public static CompanyManager Instance { get; private set; }

    // --- Configuration ---
    [Header("Company Identity")]
    public string defaultCompanyName;
    public float startingBalance;

    [Tooltip("The maximum debt allowed")]
    public float bankruptcyThreshold;

    [Header("Recurring Costs (Passive)")]
    [Tooltip("Weekly tax/insurance cost per bus in the fleet.")]
    public float weeklyCostPerBus;

    [Tooltip("Weekly food/tax cost per employee (placeholder).")]
    public float weeklyCostPerEmployee;

    // --- State Data ---
    [Header("Runtime State")]
    [SerializeField] private CompanyData _companyData;

    // --- Events ---
    public event Action<float> OnBalanceChanged;
    public event Action<Transaction> OnTransactionAdded;

    // --- Persistence ---
#if UNITY_EDITOR
    private string SavePath => Path.Combine(Application.dataPath, "company.json");
#else
    private string SavePath => Path.Combine(Application.persistentDataPath, "company.json");
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
            LoadCompanyData();
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

            // Subscribe to Time System for Passive Costs
            if (SimulationTimeManager.Instance != null)
            {
                SimulationTimeManager.Instance.OnDayChanged += CheckWeeklyExpenses;
            }
        }
        else
        {
            _companyData = new CompanyData();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            if (SimulationTimeManager.Instance != null)
            {
                SimulationTimeManager.Instance.OnDayChanged -= CheckWeeklyExpenses;
            }
        }
    }

    // --- Passive Logic (Server Only) ---

    private void CheckWeeklyExpenses()
    {
        
        int day = SimulationTimeManager.Instance.CurrentDay;

        if (day > 0 && day % 2 == 0)
        {
            ProcessWeeklyCosts();
        }
    }

    private void ProcessWeeklyCosts()
    {
        float totalBusCost = 0f;
        float totalEmpCost = 0f;

       
        if (FleetManager.Instance != null)
        {
            int busCount = FleetManager.Instance.allBuses.Count;
            totalBusCost = busCount * weeklyCostPerBus;
        }

        // Calculate Employee Costs 
        //Placeholder for now
        int employeeCount = 5;
        totalEmpCost = employeeCount * weeklyCostPerEmployee;

        // Process the Passive Transactions
        if (totalBusCost > 0)
        {
            ProcessTransaction(-totalBusCost, TransactionType.Passive, TransactionCategory.Tax,
                $"Weekly Fleet Tax ({FleetManager.Instance.allBuses.Count} buses)");
        }

        if (totalEmpCost > 0)
        {
            ProcessTransaction(-totalEmpCost, TransactionType.Passive, TransactionCategory.StaffUpkeep,
                $"Weekly Staff Food/Tax ({employeeCount} employees)");
        }
    }

    // --- Actionable Logic (Public API) ---

    
    public bool TryExecuteActionableTransaction(float amount, TransactionCategory category, string itemDescription)
    {

        if (amount < 0) return false;

        float projectedBalance = _companyData.CurrentBalance - amount;

        // Check Affordability
        if (projectedBalance < bankruptcyThreshold)
        {
            Debug.LogWarning($"[Company] Transaction declined. Bankruptcy limit ({bankruptcyThreshold}) would be exceeded.");
            return false; ;
        }

        if (IsServer)
        {
            ProcessTransaction(-amount, TransactionType.Actionable, category, itemDescription);
            return true;
        }
        else
        {
            // Client requests purchase
            RequestTransactionRpc(-amount, TransactionType.Actionable, category, itemDescription);
            return true; // Optimistic return
        }
    }

    public void AddIncome(float amount, TransactionCategory category, string description)
    {
        if (amount <= 0) return;

        if (IsServer)
        {
            ProcessTransaction(amount, TransactionType.Passive, category, description);
        }
        else
        {
            RequestTransactionRpc(amount, TransactionType.Passive, category, description);
        }
    }

    // --- Core Transaction Processing ---

    private void ProcessTransaction(float amount, TransactionType type, TransactionCategory category, string description)
    {
        _companyData.CurrentBalance += amount;

        Transaction newTrans = new Transaction
        {
            Amount = amount,
            Type = type,
            Category = category,
            Description = description,
            Timestamp = SimulationTimeManager.Instance ? SimulationTimeManager.Instance.CurrentDay : 0 // Or System.DateTime
        };

        _companyData.History.Add(newTrans);

        // Notify UI
        OnBalanceChanged?.Invoke(_companyData.CurrentBalance);
        OnTransactionAdded?.Invoke(newTrans);

        // Sync clients
        UpdateStateClientRpc(_companyData.CurrentBalance, JsonUtility.ToJson(newTrans));

        // Auto-Save on Server
        if (IsServer) SaveCompanyData();
    }

    // --- Networking ---

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer)
        {
            string json = JsonUtility.ToJson(_companyData);
            SyncFullStateRpc(json, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestTransactionRpc(float amount, TransactionType type, TransactionCategory category, string desc)
    {
        // Server validation
        if (amount < 0 && _companyData.CurrentBalance < Mathf.Abs(amount)) return;
        ProcessTransaction(amount, type, category, desc);
    }

    [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
    private void SyncFullStateRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        _companyData = JsonUtility.FromJson<CompanyData>(json);
        OnBalanceChanged?.Invoke(_companyData.CurrentBalance);
    }

    [Rpc(SendTo.NotServer)]
    private void UpdateStateClientRpc(float newBalance, string transactionJson)
    {
        _companyData.CurrentBalance = newBalance;
        Transaction t = JsonUtility.FromJson<Transaction>(transactionJson);
        _companyData.History.Add(t);

        OnBalanceChanged?.Invoke(newBalance);
        OnTransactionAdded?.Invoke(t);
    }

    // --- Persistence ---

    [ContextMenu("Save Company")]
    public void SaveCompanyData()
    {
        string json = JsonUtility.ToJson(_companyData, true);
        File.WriteAllText(SavePath, json);
    }

    [ContextMenu("Load Company")]
    public void LoadCompanyData()
    {
        if (File.Exists(SavePath))
        {
            try
            {
                string json = File.ReadAllText(SavePath);
                _companyData = JsonUtility.FromJson<CompanyData>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CompanyManager] Load failed: {e.Message}");
                ResetData();
            }
        }
        else
        {
            ResetData();
        }
    }

    private void ResetData()
    {
        _companyData = new CompanyData
        {
            CompanyName = defaultCompanyName,
            CurrentBalance = startingBalance,
            History = new List<Transaction>()
        };
    }
}

// --- Data Types ---

[Serializable]
public class CompanyData
{
    public string CompanyName;
    public float CurrentBalance;
    public List<Transaction> History = new List<Transaction>();
}

[Serializable]
public struct Transaction
{
    public float Amount;      // Positive = Income, Negative = Expense
    public TransactionType Type;
    public TransactionCategory Category;
    public string Description;
    public int Timestamp;     // The Day this happened
}

public enum TransactionType
{
    Actionable, // Initiated by Player (Buying parts, Hiring)
    Passive     // Initiated by System (Weekly tax, upkeep, automatic fines)
}

public enum TransactionCategory
{
    General,
    Grant,          // Income
    TicketRevenue,  // Income
    VehiclePurchase,// Actionable Expense
    PartPurchase,   // Actionable Expense
    Maintenance,    // Actionable/Passive (Repairs)
    Fuel,           // Passive Expense
    StaffSalary,    // Passive Expense (Monthly/Weekly)
    StaffUpkeep,    // Passive Expense (Food/Tax)
    Tax             // Passive Expense (Bus Tax)
}