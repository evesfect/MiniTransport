using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using Unity.Netcode;

[DefaultExecutionOrder(-55)] 
public class CompanyManager : NetworkBehaviour
{
    public static CompanyManager Instance { get; private set; }

    [Header("Company Identity")]
    public string defaultCompanyName;
    public float startingBalance;

    [Header("Reputation System")]
    public float GlobalSatisfaction = 80f;
    public const float MaxSatisfaction = 100f;

    public float satisfactionPenaltyPertimeout = 2.0f; 
    public float baseRewardPerPassenger = 0.5f; 

    [Tooltip("The maximum debt allowed")]
    public float bankruptcyThreshold;

    [Header("Runtime State")]
    [SerializeField] private CompanyData _companyData;

    // Buffer flags to stop disk write spam
    private bool _needsSave = false;
    private float _saveTimer = 0f;

    public CompanyData GetCompanyData() => _companyData;

    public event Action<float> OnBalanceChanged;
    public event Action<Transaction> OnTransactionAdded;
    public event Action OnWeeklyExpensesRequested;
    public event Action<float> OnSatisfactionChanged;
    public event Action OnTransferRecorded; // raised when TransferTripCount changes (KPI report refresh)

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

            if (SimulationTimeManager.Instance != null)
                SimulationTimeManager.Instance.OnDayChanged += CheckDateForBills;

            if (NetworkSyncBroker.Instance != null)
            {
                NetworkSyncBroker.Instance.OnCompanySyncTriggered += PerformStatsSync;
                NetworkSyncBroker.Instance.OnCompanyLedgerSyncTriggered += PerformLedgerSync;
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
            if (SimulationTimeManager.Instance != null)
                SimulationTimeManager.Instance.OnDayChanged -= CheckDateForBills;

            if (NetworkSyncBroker.Instance != null)
            {
                NetworkSyncBroker.Instance.OnCompanySyncTriggered -= PerformStatsSync;
                NetworkSyncBroker.Instance.OnCompanyLedgerSyncTriggered -= PerformLedgerSync;
            }
        }
    }

    private void Update()
    {
        if (IsServer && _needsSave)
        {
            _saveTimer += Time.deltaTime;
            if (_saveTimer >= 5f) 
            {
                SaveCompanyData();
                _needsSave = false;
                _saveTimer = 0f;
                
                if (NetworkSyncBroker.Instance != null)
                {
                    NetworkSyncBroker.Instance.MarkDirty(SyncDataType.CompanyLedger);
                }
            }
        }
    }

    private void CheckDateForBills()
    {
        int day = SimulationTimeManager.Instance.CurrentDay;
        if (day > 0 && day % 7 == 0)
        {
            OnWeeklyExpensesRequested?.Invoke();
        }
    }

    public void ProcessPassiveExpense(float amount, TransactionCategory category, string description)
    {
        if (amount <= 0) return;
        if (IsServer) ProcessTransaction(-amount, TransactionType.Passive, category, description);
        else RequestTransactionRpc(-amount, TransactionType.Passive, category, description);
    }

    public bool TryExecuteActionableTransaction(float amount, TransactionCategory category, string itemDescription)
    {
        if (amount < 0) return false;
        float projectedBalance = _companyData.CurrentBalance - amount;

        if (projectedBalance < bankruptcyThreshold) return false;

        if (IsServer)
        {
            ProcessTransaction(-amount, TransactionType.Actionable, category, itemDescription);
            return true;
        }
        else
        {
            RequestTransactionRpc(-amount, TransactionType.Actionable, category, itemDescription);
            return true; 
        }
    }

    public void AddIncome(float amount, TransactionCategory category, string description)
    {
        if (amount <= 0) return;
        if (IsServer) ProcessTransaction(amount, TransactionType.Passive, category, description);
        else RequestTransactionRpc(amount, TransactionType.Passive, category, description);
    }

    private void ProcessTransaction(float amount, TransactionType type, TransactionCategory category, string description)
    {
        _companyData.CurrentBalance += amount;
        int currentDay = SimulationTimeManager.Instance ? SimulationTimeManager.Instance.CurrentDay : 0;

        bool aggregated = false;
        
        // Transaction Aggregator: Combines rapid transactions into a single daily line
        if (_companyData.History.Count > 0)
        {
            // Search backwards through the ledger
            for (int i = _companyData.History.Count - 1; i >= 0; i--)
            {
                var tx = _companyData.History[i];
                
                // Optimization: Stop searching if we hit a transaction from yesterday
                if (tx.Timestamp != currentDay) break;

                // Match by category and type
                if (tx.Type == type && tx.Category == category)
                {
                    tx.Amount += amount;
                    tx.Count += 1; // Increment the times this happened
                    _companyData.History[i] = tx; 
                    aggregated = true;
                    break;
                }
            }
        }

        if (!aggregated)
        {
            Transaction newTrans = new Transaction
            {
                Amount = amount,
                Type = type,
                Category = category,
                Description = description,
                Timestamp = currentDay,
                Count = 1 // Starts at 1
            };

            _companyData.History.Add(newTrans);
            OnTransactionAdded?.Invoke(newTrans);
        }

        OnBalanceChanged?.Invoke(_companyData.CurrentBalance);

        if (NetworkSyncBroker.Instance != null)
        {
            NetworkSyncBroker.Instance.MarkDirty(SyncDataType.CompanyStats);
            
            // Only force an immediate network sync if it's a new line, 
            // aggregated data will sync automatically every 5 seconds with the save timer
            if (!aggregated) 
            {
                NetworkSyncBroker.Instance.MarkDirty(SyncDataType.CompanyLedger);
            }
        }

        _needsSave = true;
    }

    /// <summary>
    /// Server-only. Records that <paramref name="count"/> passengers made a transfer,
    /// feeding the global "Number of Transfer Trips" KPI.
    /// </summary>
    public void RecordTransfer(int count)
    {
        if (!IsServer || count <= 0) return;

        _companyData.TransferTripCount += count;

        // Company stats are surfaced to the local dashboard via OnBalanceChanged
        // (the dashboard rebuilds the full stats snapshot); the balance value is unchanged.
        OnBalanceChanged?.Invoke(_companyData.CurrentBalance);

        if (NetworkSyncBroker.Instance != null)
            NetworkSyncBroker.Instance.MarkDirty(SyncDataType.CompanyStats);

        OnTransferRecorded?.Invoke(); // let KPIManager refresh the transfer-trip report value

        _needsSave = true;
    }

    public void ModifySatisfaction(float amount)
    {
        GlobalSatisfaction = Mathf.Clamp(GlobalSatisfaction + amount, 0f, MaxSatisfaction);
        //Debug.Log($"[Company] Satisfaction updated: {GlobalSatisfaction:F1}% ({amount:F1})");
        OnSatisfactionChanged?.Invoke(GlobalSatisfaction);
    }

    private void PerformStatsSync(BaseRpcTarget target)
    {
        var stats = new CompanyStatsData
        {
            currentBalance = _companyData.CurrentBalance,
            transferTripCount = _companyData.TransferTripCount
        };
        SyncStatsRpc(JsonUtility.ToJson(stats), target);
    }

    private void PerformLedgerSync(BaseRpcTarget target)
    {
        var ledger = new CompanyLedgerData { transactions = _companyData.History };
        SyncLedgerRpc(JsonUtility.ToJson(ledger), target);
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

    [Rpc(SendTo.SpecifiedInParams)]
    private void SyncStatsRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        
        var stats = JsonUtility.FromJson<CompanyStatsData>(json);
        _companyData.CurrentBalance = stats.currentBalance;
        _companyData.TransferTripCount = stats.transferTripCount;

        OnBalanceChanged?.Invoke(stats.currentBalance);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void SyncLedgerRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        var ledger = JsonUtility.FromJson<CompanyLedgerData>(json);
        _companyData.History = ledger.transactions;
    }

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
        else ResetData();
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

[Serializable]
public class CompanyData
{
    public string CompanyName;
    public float CurrentBalance;
    public int TransferTripCount; // Global KPI: cumulative number of passenger transfers
    public List<Transaction> History = new List<Transaction>();
}

[Serializable]
public struct Transaction
{
    public float Amount;      
    public TransactionType Type;
    public TransactionCategory Category;
    public string Description;
    public int Timestamp;     
    public int Count; // Tracks how many times this specific transaction occurred
}

public enum TransactionType { Actionable, Passive }

public enum TransactionCategory
{
    General, Grant, TicketRevenue, VehiclePurchase, PartPurchase, Maintenance, Fuel, StaffSalary, StaffUpkeep, Tax
}


