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

    [Header("Reputation System")]
    public float GlobalSatisfaction = 80f;
    public const float MaxSatisfaction = 100f;

    public float satisfactionPenaltyPertimeout = 2.0f; // -2% if someone leaves angry
    public float baseRewardPerPassenger = 0.5f; // +0.5% per happy passenger

    [Tooltip("The maximum debt allowed")]
    public float bankruptcyThreshold;

    // --- State Data ---
    [Header("Runtime State")]
    [SerializeField] private CompanyData _companyData;

    // --- Events ---
    public event Action<float> OnBalanceChanged;
    public event Action<Transaction> OnTransactionAdded;
    public event Action OnWeeklyExpensesRequested;
    public event Action<float> OnSatisfactionChanged;

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

            if (SimulationTimeManager.Instance != null)
            {
                SimulationTimeManager.Instance.OnDayChanged += CheckDateForBills;

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
                SimulationTimeManager.Instance.OnDayChanged -= CheckDateForBills;

        }
    }

    // --- Trigger ---

    private void CheckDateForBills()
    {
        // Logic: If it's a new week, yell at everyone to send their bills
        int day = SimulationTimeManager.Instance.CurrentDay;

        if (day > 0 && day % 7 == 0)
        {
            Debug.Log("[Company] Weekly Expenses Requested.");
            OnWeeklyExpensesRequested?.Invoke();
        }
    }


    //Used by other managers to send their recurring costs
    public void ProcessPassiveExpense(float amount, TransactionCategory category, string description)
    {
        if (amount <= 0) return; // Expense must be positive number (we negate internally)

        if (IsServer)
        {
            ProcessTransaction(-amount, TransactionType.Passive, category, description);
        }
        else
        {
            RequestTransactionRpc(-amount, TransactionType.Passive, category, description);
        }
    }

    

    /// <summary>
    /// Used by Player for buying items. Checks Bankruptcy threshold.
    /// </summary>
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

    /// <summary>
    /// Used for Income (Tickets, Grants).
    /// </summary>
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

    // Reputation System
    public void ModifySatisfaction(float amount)
    {
        GlobalSatisfaction = Mathf.Clamp(GlobalSatisfaction + amount, 0f, MaxSatisfaction);
        Debug.Log($"[Company] Satisfaction updated: {GlobalSatisfaction:F1}% ({amount:F1})");
        OnSatisfactionChanged?.Invoke(GlobalSatisfaction);
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
        if (type == TransactionType.Actionable && amount < 0)
        {
            if (_companyData.CurrentBalance + amount < bankruptcyThreshold) return;
        }
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