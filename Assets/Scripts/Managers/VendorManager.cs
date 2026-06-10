using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public struct VendorItemStats
{
    public float Reliability;
    public float SpeedMultiplier;
    public float PriceMultiplier;
    public float Durability; 
}

[DefaultExecutionOrder(-49)] 
public class VendorManager : NetworkBehaviour
{
    public static VendorManager Instance { get; private set; }

    [Header("Database")]
    public List<VendorData> availableVendors = new List<VendorData>();
    public List<ActiveDeal> activeDeals = new List<ActiveDeal>();
    public List<ActiveOrder> activeOrders = new List<ActiveOrder>();
    public List<ItemCounter> lifetimeItemCounts = new List<ItemCounter>();

    [Header("Settings")]
    public float contractCancellationFine = 500f;
    public float xpPerDelivery = 10f;
    public float maxLoyaltyLevel = 5;
    public float baseDeliveryHours = 12f; 

    public event Action OnVendorDataUpdated;

    public static readonly Dictionary<BusPartCategory, string[]> CategoryParts = new Dictionary<BusPartCategory, string[]>
    {
        { BusPartCategory.Engine, new string[] { "EngineBlock", "Piston", "Alternator" } },
        { BusPartCategory.Tires, new string[] { "StandardTire", "WinterTire", "HeavyDutyTire" } },
        { BusPartCategory.Chassis, new string[] { "BusFrame", "Axle", "DoorAssembly" } },
        { BusPartCategory.Electronics, new string[] { "Dashboard", "SensorArray", "WiringHarness" } }
    };

#if UNITY_EDITOR
    private string SavePath => Path.Combine(Application.dataPath, "vendors.json");
#else
    private string SavePath => Path.Combine(Application.persistentDataPath, "vendors.json");
#endif

    private float CurrentAbsoluteHour => SimulationTimeManager.Instance.CurrentDay * 24f + SimulationTimeManager.Instance.CurrentTimeOfDay;

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
            if (availableVendors.Count == 0) GenerateWeeklyVendors();
            
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            SimulationTimeManager.Instance.OnHourChanged += ProcessActiveOrders;
            if (CompanyManager.Instance != null) CompanyManager.Instance.OnWeeklyExpensesRequested += GenerateWeeklyVendors;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            if (SimulationTimeManager.Instance != null)
                SimulationTimeManager.Instance.OnHourChanged -= ProcessActiveOrders;
            if (CompanyManager.Instance != null)
                CompanyManager.Instance.OnWeeklyExpensesRequested -= GenerateWeeklyVendors;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer)
        {
            SyncVendorsRpc(SerializeVendors(), RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
    }

    public VendorItemStats GetItemStats(string vendorID, string baseItemName)
    {
        var vendor = availableVendors.FirstOrDefault(v => v.VendorID == vendorID);
        if (vendor == null) return new VendorItemStats();

        int seed = vendorID.GetHashCode() ^ baseItemName.GetHashCode();
        System.Random rand = new System.Random(seed);

        float RandomRange(float min, float max) => min + (float)rand.NextDouble() * (max - min);

        return new VendorItemStats {
            Reliability = Mathf.Clamp(vendor.ReliabilityScore + RandomRange(-15f, 15f), 0f, 100f),
            SpeedMultiplier = Mathf.Max(0.1f, vendor.DeliverySpeedMultiplier + RandomRange(-0.3f, 0.3f)),
            PriceMultiplier = Mathf.Max(0.1f, vendor.PriceMultiplier + RandomRange(-0.3f, 0.3f)),
            Durability = Mathf.Clamp(RandomRange(vendor.MinDurability, vendor.MaxDurability), 0f, 100f)
        };
    }

    private void ProcessActiveOrders()
    {
        if (!IsServer || activeOrders.Count == 0) return;

        List<ActiveOrder> completed = new List<ActiveOrder>();

        foreach (var order in activeOrders)
        {
            if (CurrentAbsoluteHour >= order.ActualArrivalHour)
            {
                // Fallback for old save files where Amount might be 0
                int actualAmount = Mathf.Max(1, order.Amount);

                for (int i = 0; i < actualAmount; i++)
                {
                    string specificID = (order.Amount > 0 && !string.IsNullOrEmpty(order.BaseItemName)) 
                        ? $"{order.BaseItemName}{order.StartIndex + i}" 
                        : order.ItemID;
                        
                    InventoryManager.Instance.AddPartWithDurability(specificID, order.DurabilityRoll);
                }
                
                if (order.IsDelayed) Debug.Log($"[Vendor] {order.ItemID} ({actualAmount}x) arrived LATE from {order.VendorID}.");
                else Debug.Log($"[Vendor] {order.ItemID} ({actualAmount}x) arrived ON TIME from {order.VendorID}.");
                
                var vendor = availableVendors.FirstOrDefault(v => v.VendorID == order.VendorID);
                if (vendor != null)
                {
                    vendor.CurrentXP += xpPerDelivery;
                    if (vendor.CurrentXP >= 100f && vendor.LoyaltyLevel < maxLoyaltyLevel)
                    {
                        vendor.LoyaltyLevel++;
                        vendor.CurrentXP = 0;
                        vendor.PriceMultiplier = Mathf.Max(0.5f, vendor.PriceMultiplier - 0.05f);
                    }
                }
                completed.Add(order);
            }
        }

        if (completed.Count > 0)
        {
            foreach (var c in completed) activeOrders.Remove(c);
            SaveVendors();
            SyncVendorsRpc(SerializeVendors());
        }
    }

    private void GenerateWeeklyVendors()
    {
        List<VendorData> newMarket = new List<VendorData>();

        foreach (var deal in activeDeals)
        {
            var existing = availableVendors.FirstOrDefault(v => v.VendorID == deal.VendorID);
            if (existing != null) newMarket.Add(existing);
        }

        string[] prefixes = { "Apex", "Global", "Budget", "Rapid", "Prime", "Metro" };
        string[] suffixes = { "Parts", "Motors", "Logistics", "Supplies", "Tech" };

        foreach (BusPartCategory cat in Enum.GetValues(typeof(BusPartCategory)))
        {
            if (cat == BusPartCategory.None) continue;
            newMarket.Add(GenerateRandomVendor(cat, VendorQuality.Low, prefixes, suffixes));
            newMarket.Add(GenerateRandomVendor(cat, VendorQuality.Mid, prefixes, suffixes));
            newMarket.Add(GenerateRandomVendor(cat, VendorQuality.High, prefixes, suffixes));
        }

        availableVendors = newMarket;
        SaveVendors();
        SyncVendorsRpc(SerializeVendors());
    }

    private VendorData GenerateRandomVendor(BusPartCategory cat, VendorQuality q, string[] pre, string[] suf)
    {
        VendorData v = new VendorData { 
            VendorID = Guid.NewGuid().ToString().Substring(0, 8),
            Category = cat,
            QualityTier = q,
            Name = $"{pre[UnityEngine.Random.Range(0, pre.Length)]} {suf[UnityEngine.Random.Range(0, suf.Length)]}"
        };

        switch (q)
        {
            case VendorQuality.Low:
                v.ReliabilityScore = UnityEngine.Random.Range(40f, 65f);
                v.DeliverySpeedMultiplier = UnityEngine.Random.Range(1.2f, 1.8f); 
                v.PriceMultiplier = UnityEngine.Random.Range(0.5f, 0.8f);
                v.MinDurability = 30f; v.MaxDurability = 60f;
                break;
            case VendorQuality.Mid:
                v.ReliabilityScore = UnityEngine.Random.Range(70f, 85f);
                v.DeliverySpeedMultiplier = UnityEngine.Random.Range(0.8f, 1.2f);
                v.PriceMultiplier = UnityEngine.Random.Range(0.9f, 1.1f);
                v.MinDurability = 60f; v.MaxDurability = 85f;
                break;
            case VendorQuality.High:
                v.ReliabilityScore = UnityEngine.Random.Range(90f, 100f);
                v.DeliverySpeedMultiplier = UnityEngine.Random.Range(0.5f, 0.8f); 
                v.PriceMultiplier = UnityEngine.Random.Range(1.3f, 1.8f);
                v.MinDurability = 85f; v.MaxDurability = 100f;
                break;
        }
        return v;
    }

    public void SignDeal(string vendorID, BusPartCategory category)
    {
        if (IsServer) SignDealInternal(vendorID, category);
        else RequestSignDealRpc(vendorID, category);
    }

    public void CancelDeal(string vendorID)
    {
        if (IsServer) CancelDealInternal(vendorID);
        else RequestCancelDealRpc(vendorID);
    }

    public void PlaceOrder(string vendorID, string baseItemName, int amount = 1)
    {
        if (IsServer) PlaceOrderInternal(vendorID, baseItemName, amount);
        else RequestPlaceOrderRpc(vendorID, baseItemName, amount);
    }

    private void SignDealInternal(string vendorID, BusPartCategory category)
    {
        if (activeDeals.Count(d => d.Category == category) >= 2) return; 
        if (activeDeals.Any(d => d.VendorID == vendorID)) return; 

        activeDeals.Add(new ActiveDeal { Category = category, VendorID = vendorID, StartDay = SimulationTimeManager.Instance.CurrentDay });
        SaveVendors();
        SyncVendorsRpc(SerializeVendors());
    }

    private void CancelDealInternal(string vendorID)
    {
        if (activeOrders.Any(o => o.VendorID == vendorID))
        {
            Debug.LogWarning($"[Vendor] Cannot cancel deal with {vendorID}. There are active orders.");
            return;
        }

        var deal = activeDeals.FirstOrDefault(d => d.VendorID == vendorID);
        if (deal != null)
        {
            if (SimulationTimeManager.Instance.CurrentDay - deal.StartDay < 7)
                CompanyManager.Instance.TryExecuteActionableTransaction(contractCancellationFine, TransactionCategory.General, "Contract Cancellation Fee");
            
            activeDeals.Remove(deal);
            availableVendors.RemoveAll(v => v.VendorID == vendorID);
            
            SaveVendors();
            SyncVendorsRpc(SerializeVendors());
        }
    }

    private void PlaceOrderInternal(string vendorID, string baseItemName, int amount)
    {
        amount = Mathf.Clamp(amount, 1, 50); // Sanity check

        var vendor = availableVendors.FirstOrDefault(v => v.VendorID == vendorID);
        if (vendor == null) return;
        if (activeOrders.Count(o => o.Category == vendor.Category) >= 2) return; 

        var itemStats = GetItemStats(vendorID, baseItemName);

        float estHours = baseDeliveryHours * itemStats.SpeedMultiplier;
        float expectedTime = CurrentAbsoluteHour + estHours;
        
        bool isDelayed = UnityEngine.Random.Range(0f, 100f) > itemStats.Reliability;
        float actualTime = expectedTime;
        if (isDelayed) actualTime += UnityEngine.Random.Range(12f, 48f);

        var counter = lifetimeItemCounts.FirstOrDefault(c => c.BaseName == baseItemName);
        if (counter == null)
        {
            counter = new ItemCounter { BaseName = baseItemName, Count = 0 };
            lifetimeItemCounts.Add(counter);
        }

        int startIdx = counter.Count + 1;
        counter.Count += amount;
        int endIdx = counter.Count;

        string generatedDisplayID = amount == 1 ? $"{baseItemName}{startIdx}" : $"{baseItemName}{startIdx}-{endIdx}";

        activeOrders.Add(new ActiveOrder {
            OrderID = Guid.NewGuid().ToString().Substring(0, 6),
            VendorID = vendorID,
            ItemID = baseItemName,
            Category = vendor.Category,
            ExpectedArrivalHour = expectedTime,
            ActualArrivalHour = actualTime,
            IsDelayed = isDelayed,
            DurabilityRoll = itemStats.Durability 
        });
        
        CompanyManager.Instance.TryExecuteActionableTransaction(100f * itemStats.PriceMultiplier * amount, TransactionCategory.PartPurchase, $"Ordered {generatedDisplayID}");

        SaveVendors();
        SyncVendorsRpc(SerializeVendors());

        // NEW: Tell Request Manager parts were ordered, passing the base item name.
        if (RequestManager.Instance != null)
        {
            RequestManager.Instance.NotifyActionTaken(RequestType.BuyParts, amount, baseItemName);
        }
    }

    [Rpc(SendTo.Server)] private void RequestSignDealRpc(string vID, BusPartCategory c) { SignDealInternal(vID, c); }
    [Rpc(SendTo.Server)] private void RequestCancelDealRpc(string vID) { CancelDealInternal(vID); }
    [Rpc(SendTo.Server)] private void RequestPlaceOrderRpc(string vID, string iID, int amount) { PlaceOrderInternal(vID, iID, amount); }

    [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
private void SyncVendorsRpc(string json, RpcParams rpcParams = default)
{
    if (IsServer)
    {
        // The Host already updated its lists directly. It just needs the UI event to fire.
        OnVendorDataUpdated?.Invoke();
        return; 
    }
    
    // Clients need to deserialize the JSON first
    var container = JsonUtility.FromJson<VendorContainer>(json);
    if (container != null)
    {
        availableVendors = container.AvailableVendors;
        activeDeals = container.ActiveDeals;
        activeOrders = container.ActiveOrders;
        lifetimeItemCounts = container.LifetimeItemCounts;
        OnVendorDataUpdated?.Invoke();
    }
}

    private string SerializeVendors() { return JsonUtility.ToJson(new VendorContainer { AvailableVendors = availableVendors, ActiveDeals = activeDeals, ActiveOrders = activeOrders, LifetimeItemCounts = lifetimeItemCounts }, true); }
    public void SaveVendors() { File.WriteAllText(SavePath, SerializeVendors()); }
    public void LoadVendors()
    {
        if (File.Exists(SavePath))
        {
            var container = JsonUtility.FromJson<VendorContainer>(File.ReadAllText(SavePath));
            if (container != null)
            {
                availableVendors = container.AvailableVendors ?? new List<VendorData>();
                activeDeals = container.ActiveDeals ?? new List<ActiveDeal>();
                activeOrders = container.ActiveOrders ?? new List<ActiveOrder>();
                lifetimeItemCounts = container.LifetimeItemCounts ?? new List<ItemCounter>();
            }
        }
    }
}