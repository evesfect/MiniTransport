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

    public float satisfactionPenaltyPertimeout = 2.0f; // -2% if someone leaves angry
    public float baseRewardPerPassenger = 0.5f; // +0.5% per happy passenger

    [Tooltip("The maximum debt allowed")]
    public float bankruptcyThreshold;

    [Header("Runtime State")]
    [SerializeField] private CompanyData _companyData;

    public CompanyData GetCompanyData() => _companyData;

    public event Action<float> OnBalanceChanged;
    public event Action<Transaction> OnTransactionAdded;
    public event Action OnWeeklyExpensesRequested;
    public event Action<float> OnSatisfactionChanged;

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

        Transaction newTrans = new Transaction
        {
            Amount = amount,
            Type = type,
            Category = category,
            Description = description,
            Timestamp = SimulationTimeManager.Instance ? SimulationTimeManager.Instance.CurrentDay : 0 
        };

        _companyData.History.Add(newTrans);

        OnBalanceChanged?.Invoke(_companyData.CurrentBalance);
        OnTransactionAdded?.Invoke(newTrans);

        if (NetworkSyncBroker.Instance != null)
        {
            NetworkSyncBroker.Instance.MarkDirty(SyncDataType.CompanyStats);
            NetworkSyncBroker.Instance.MarkDirty(SyncDataType.CompanyLedger);
        }

        if (IsServer) SaveCompanyData();
    }

    // Reputation System
    public void ModifySatisfaction(float amount)
    {
        GlobalSatisfaction = Mathf.Clamp(GlobalSatisfaction + amount, 0f, MaxSatisfaction);
        Debug.Log($"[Company] Satisfaction updated: {GlobalSatisfaction:F1}% ({amount:F1})");
        OnSatisfactionChanged?.Invoke(GlobalSatisfaction);
    }


    // Reputation System
    public void ModifySatisfaction(float amount)
    {
        GlobalSatisfaction = Mathf.Clamp(GlobalSatisfaction + amount, 0f, MaxSatisfaction);
        Debug.Log($"[Company] Satisfaction updated: {GlobalSatisfaction:F1}% ({amount:F1})");
        OnSatisfactionChanged?.Invoke(GlobalSatisfaction);
    }

    private void PerformStatsSync(BaseRpcTarget target)
    {
        var stats = new CompanyStatsData { currentBalance = _companyData.CurrentBalance };
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
}

public enum TransactionType { Actionable, Passive }

public enum TransactionCategory
{
    General, Grant, TicketRevenue, VehiclePurchase, PartPurchase, Maintenance, Fuel, StaffSalary, StaffUpkeep, Tax
}