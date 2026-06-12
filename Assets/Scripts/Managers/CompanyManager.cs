using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using Unity.Collections;
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

    [Header("Fare Settings")]
    [Tooltip("Starting ticket price charged per passenger per route leg (flat, length-independent). The GM can change it at runtime.")]
    public float defaultTicketPrice = 2.0f;
    [Tooltip("Starting transfer discount (0..1) applied to fares on transfer legs. The GM can change it at runtime.")]
    [Range(0f, 1f)] public float defaultTransferDiscount = 0.5f;

    [Tooltip("The maximum debt allowed")]
    public float bankruptcyThreshold;

    [Header("Runtime State")]
    [SerializeField] private CompanyData _companyData;

    // Buffer flags to stop disk write spam
    private bool _needsSave = false;
    private float _saveTimer = 0f;

    // Mirrors only the most recent expense (negative transaction) to every client, so the HUD can
    // show it without subscribing to the whole ledger. Written by the server.
    private readonly NetworkVariable<FixedString128Bytes> _netLastExpenseDesc = new NetworkVariable<FixedString128Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private readonly NetworkVariable<float> _netLastExpenseAmount = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // GM-tunable fare values + a networked mirror of satisfaction, so the GM panel works on clients.
    private readonly NetworkVariable<float> _netTicketPrice = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _netTransferDiscount = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _netSatisfaction = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public CompanyData GetCompanyData() => _companyData;

    /// <summary>Description of the most recent expense (empty if none recorded yet).</summary>
    public string LatestExpenseDescription => _netLastExpenseDesc.Value.ToString();
    /// <summary>Signed amount of the most recent expense (negative). 0 means none recorded yet.</summary>
    public float LatestExpenseAmount => _netLastExpenseAmount.Value;
    /// <summary>True once at least one expense has been recorded.</summary>
    public bool HasLatestExpense => _netLastExpenseAmount.Value < 0f;

    /// <summary>Current ticket price per passenger per route leg. Read-Everyone (works on clients).</summary>
    public float TicketPrice => _netTicketPrice.Value;
    /// <summary>Current transfer discount (0..1) applied to transfer-leg fares. Read-Everyone.</summary>
    public float TransferDiscount => _netTransferDiscount.Value;
    /// <summary>Networked mirror of GlobalSatisfaction so the GM panel reads it on clients too.</summary>
    public float Satisfaction => _netSatisfaction.Value;

    public event Action<float> OnBalanceChanged;
    public event Action<Transaction> OnTransactionAdded;
    public event Action OnWeeklyExpensesRequested;
    public event Action<float> OnSatisfactionChanged;
    public event Action OnLedgerUpdated;
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
            InitializeLatestExpenseFromHistory();

            _netTicketPrice.Value = Mathf.Max(0f, defaultTicketPrice);
            _netTransferDiscount.Value = Mathf.Clamp01(defaultTransferDiscount);
            _netSatisfaction.Value = GlobalSatisfaction;

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
        if (!IsServer || !_needsSave) return;

        _saveTimer += Time.deltaTime;
        if (_saveTimer < 5f) return;

        // Reset the timer up front, regardless of outcome. If the write fails (e.g. the file is
        // briefly locked by OneDrive sync), we retry on the NEXT 5s interval instead of hammering
        // the locked file every frame — which is what turned a single transient lock into an
        // endless IOException storm.
        _saveTimer = 0f;

        if (TrySaveCompanyData())
        {
            _needsSave = false;

            if (NetworkSyncBroker.Instance != null)
            {
                NetworkSyncBroker.Instance.MarkDirty(SyncDataType.CompanyLedger);
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

        if (amount < 0f) SetLatestExpense(description, amount);

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
        OnLedgerUpdated?.Invoke();
    }

    // Server-only. Pushes the latest expense to the networked mirror read by the HUD.
    private void SetLatestExpense(string description, float amount)
    {
        if (!IsServer || !IsSpawned) return;

        FixedString128Bytes desc = default;
        if (!string.IsNullOrEmpty(description)) desc.CopyFromTruncated(description);

        _netLastExpenseDesc.Value = desc;
        _netLastExpenseAmount.Value = amount;
    }

    // Server-only. Seeds the latest-expense mirror from the most recent negative entry in the
    // loaded ledger, so a freshly loaded save shows the last expense from company.json immediately.
    private void InitializeLatestExpenseFromHistory()
    {
        if (!IsServer || _companyData?.History == null) return;

        for (int i = _companyData.History.Count - 1; i >= 0; i--)
        {
            if (_companyData.History[i].Amount < 0f)
            {
                SetLatestExpense(_companyData.History[i].Description, _companyData.History[i].Amount);
                return;
            }
        }
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
        if (IsServer && IsSpawned) _netSatisfaction.Value = GlobalSatisfaction;
        OnSatisfactionChanged?.Invoke(GlobalSatisfaction);
    }

    // --- GM fare controls (callable from any client, e.g. the GM panel) ---

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetTicketPriceRpc(float price)
    {
        _netTicketPrice.Value = Mathf.Max(0f, price);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetTransferDiscountRpc(float discount)
    {
        _netTransferDiscount.Value = Mathf.Clamp01(discount);
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
        OnLedgerUpdated?.Invoke();
    }

    [ContextMenu("Save Company")]
    public void SaveCompanyData() => TrySaveCompanyData();

    // Returns true on success. Failures are swallowed and logged once so a transient file lock
    // (OneDrive sync touching the Assets copy) doesn't throw an unhandled exception every frame.
    private bool TrySaveCompanyData()
    {
        try
        {
            string json = JsonUtility.ToJson(_companyData, true);
            File.WriteAllText(SavePath, json);
            return true;
        }
        catch (IOException e)
        {
            Debug.LogWarning($"[CompanyManager] Save deferred (file busy), will retry next interval: {e.Message}");
            return false;
        }
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
        OnLedgerUpdated?.Invoke();
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


