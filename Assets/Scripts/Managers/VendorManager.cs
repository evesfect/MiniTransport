using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-49)] // Initialize just after EmployeeManager
public class VendorManager : NetworkBehaviour
{
    public static VendorManager Instance { get; private set; }

    [Header("Database")]
    public List<VendorData> allVendors = new List<VendorData>();
    public List<ActiveDeal> activeDeals = new List<ActiveDeal>();

    [Header("Settings")]
    public float contractCancellationFine = 500f;
    public float xpPerTimelyDelivery = 10f;
    public float xpPenaltyLateDelivery = -15f;
    public float maxLoyaltyLevel = 5;

    // Events for UI
    public event Action OnVendorDataUpdated;

#if UNITY_EDITOR
    private string SavePath => Path.Combine(Application.dataPath, "vendors.json");
#else
    private string SavePath => Path.Combine(Application.persistentDataPath, "vendors.json");
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
            LoadVendors();
            
            // If first time run, generate default vendors
            if (allVendors.Count == 0) GenerateDefaultVendors();
            
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
        else
        {
            allVendors.Clear();
            activeDeals.Clear();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    // --- Core Logic: Deals ---

    /// <summary>
    /// Signs a deal with a vendor for a specific category. 
    /// If a deal already exists, it cancels it (applying a fine).
    /// </summary>
    public void SignDeal(string vendorID, BusPartCategory category)
    {
        if (IsServer) SignDealInternal(vendorID, category);
        else RequestSignDealRpc(vendorID, category);
    }

    public void CancelDeal(BusPartCategory category)
    {
        if (IsServer) CancelDealInternal(category);
        else RequestCancelDealRpc(category);
    }

    /// <summary>
    /// Called when the player buys a part. Calculates if the delivery is successful/timely.
    /// </summary>
    /// <returns>True if delivery is "Timely", False if "Delayed"</returns>
    public bool ProcessOrder(BusPartCategory category)
    {
        // Find who supplies this category
        var deal = activeDeals.FirstOrDefault(d => d.Category == category);
        if (deal == null) 
        {
            Debug.LogWarning("No vendor assigned for " + category);
            return true; // Default to success if no vendor, or block purchase (Design choice)
        }

        var vendor = allVendors.FirstOrDefault(v => v.VendorID == deal.VendorID);
        if (vendor == null) return true;

        // Logic: Random check against reliability
        // If Reliability is 80, there is a 20% chance of delay.
        float roll = UnityEngine.Random.Range(0f, 100f);
        bool isTimely = roll <= vendor.ReliabilityScore;

        if (IsServer)
        {
            UpdateVendorReputation(vendor, isTimely);
        }
        else
        {
            RequestReputationUpdateRpc(vendor.VendorID, isTimely);
        }

        return isTimely;
    }

    // --- Internal Logic (Server) ---

    private void GenerateDefaultVendors()
    {
        // 1. Apex Parts (Expensive, Reliable)
        allVendors.Add(new VendorData {
            VendorID = "V_APEX", Name = "Apex Engineering", 
            Description = "Premium parts, premium prices. Never late.",
            ReliabilityScore = 95f, PriceMultiplier = 1.5f, Specialty = BusPartCategory.Engine
        });

        // 2. Budget Bits (Cheap, Unreliable)
        allVendors.Add(new VendorData {
            VendorID = "V_BUDGET", Name = "Budget Bus Bits", 
            Description = "We get it there... eventually. Great prices.",
            ReliabilityScore = 60f, PriceMultiplier = 0.7f, Specialty = BusPartCategory.Chassis
        });

        // 3. Standard Spares (Balanced)
        allVendors.Add(new VendorData {
            VendorID = "V_STD", Name = "Standard Spares Inc.", 
            Description = "The market standard. Reliable enough.",
            ReliabilityScore = 80f, PriceMultiplier = 1.0f, Specialty = BusPartCategory.Tires
        });

        SaveVendors();
    }

    private void SignDealInternal(string vendorID, BusPartCategory category)
    {
        // 1. Check for existing deal
        var existingDeal = activeDeals.FirstOrDefault(d => d.Category == category);
        if (existingDeal != null)
        {
            // If we are already with this vendor, do nothing
            if (existingDeal.VendorID == vendorID) return;

            // Otherwise, we are breaking a contract! Apply Fine.
            Debug.Log($"[Vendor] Breaking contract with {existingDeal.VendorID} for {category}");
            if (CompanyManager.Instance != null)
            {
                CompanyManager.Instance.TryExecuteActionableTransaction(contractCancellationFine, TransactionCategory.General, "Contract Cancellation Fine");
            }
            activeDeals.Remove(existingDeal);
        }

        // 2. Add new deal
        activeDeals.Add(new ActiveDeal {
            Category = category,
            VendorID = vendorID,
            StartDate = DateTime.Now.ToString()
        });

        Debug.Log($"[Vendor] Signed deal with {vendorID} for {category}");
        SaveVendors();
        SyncVendorsRpc(SerializeVendors());
    }

    private void CancelDealInternal(BusPartCategory category)
    {
        var existingDeal = activeDeals.FirstOrDefault(d => d.Category == category);
        if (existingDeal != null)
        {
            if (CompanyManager.Instance != null)
            {
                CompanyManager.Instance.TryExecuteActionableTransaction(contractCancellationFine, TransactionCategory.General, "Contract Cancellation Fine");
            }
            activeDeals.Remove(existingDeal);
            
            SaveVendors();
            SyncVendorsRpc(SerializeVendors());
        }
    }

    private void UpdateVendorReputation(VendorData vendor, bool positive)
    {
        if (positive)
        {
            vendor.CurrentXP += xpPerTimelyDelivery;
            // Level Up Logic
            if (vendor.CurrentXP >= 100f && vendor.LoyaltyLevel < maxLoyaltyLevel)
            {
                vendor.LoyaltyLevel++;
                vendor.CurrentXP = 0; // Reset or carry over
                // Reward: Better price?
                vendor.PriceMultiplier -= 0.05f; // 5% discount per level
                Debug.Log($"{vendor.Name} leveled up! New Price Multiplier: {vendor.PriceMultiplier}");
            }
            // Cap Reliability
            vendor.ReliabilityScore = Mathf.Min(100f, vendor.ReliabilityScore + 1f);
        }
        else
        {
            vendor.CurrentXP = Mathf.Max(0, vendor.CurrentXP + xpPenaltyLateDelivery);
            vendor.ReliabilityScore = Mathf.Max(0f, vendor.ReliabilityScore - 2f); // Drop reliability
        }

        SaveVendors();
        SyncVendorsRpc(SerializeVendors());
    }

    // --- Networking ---

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer) SyncVendorsRpc(SerializeVendors(), RpcTarget.Single(clientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.Server)]
    private void RequestSignDealRpc(string vendorID, BusPartCategory category) { SignDealInternal(vendorID, category); }

    [Rpc(SendTo.Server)]
    private void RequestCancelDealRpc(BusPartCategory category) { CancelDealInternal(category); }

    [Rpc(SendTo.Server)]
    private void RequestReputationUpdateRpc(string vendorID, bool positive) 
    {
        var v = allVendors.FirstOrDefault(x => x.VendorID == vendorID);
        if (v != null) UpdateVendorReputation(v, positive);
    }

    [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
    private void SyncVendorsRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        var container = JsonUtility.FromJson<VendorContainer>(json);
        if (container != null)
        {
            allVendors = container.AllVendors;
            activeDeals = container.ActiveDeals;
            OnVendorDataUpdated?.Invoke();
        }
    }

    // --- Persistence ---

    private string SerializeVendors()
    {
        return JsonUtility.ToJson(new VendorContainer
        {
            AllVendors = allVendors,
            ActiveDeals = activeDeals
        }, true);
    }

    [ContextMenu("Save")]
    public void SaveVendors()
    {
        File.WriteAllText(SavePath, SerializeVendors());
    }

    [ContextMenu("Load")]
    public void LoadVendors()
    {
        if (File.Exists(SavePath))
        {
            var container = JsonUtility.FromJson<VendorContainer>(File.ReadAllText(SavePath));
            if (container != null)
            {
                allVendors = container.AllVendors ?? new List<VendorData>();
                activeDeals = container.ActiveDeals ?? new List<ActiveDeal>();
            }
        }
    }
}