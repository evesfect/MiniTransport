using System;
using System.Collections.Generic;
using UnityEngine;

public enum BusPartCategory { None, Engine, Tires, Chassis, Electronics }
public enum VendorQuality { Low, Mid, High }

[Serializable]
public class VendorData
{
    public string VendorID;
    public string Name;
    public BusPartCategory Category; 
    public VendorQuality QualityTier;

    [Range(0, 100)] public float ReliabilityScore; 
    public float DeliverySpeedMultiplier;          
    public float PriceMultiplier; 
    
    public float MinDurability;
    public float MaxDurability;

    public int LoyaltyLevel;       
    public float CurrentXP;        
}

[Serializable]
public class ActiveDeal
{
    public BusPartCategory Category;
    public string VendorID;          
    public int StartDay; 
}

[Serializable]
public class ActiveOrder
{
    public string OrderID;
    public string VendorID;
    public string ItemID;        // Display name, e.g. "Tire3" or "Tire4-13"
    
    public string BaseItemName;  // Added for bulk extraction upon delivery
    public int StartIndex;       // The starting ID number
    public int Amount;           // Number of items ordered
    
    public BusPartCategory Category;
    
    public float ExpectedArrivalHour; // The initial fast timer
    public float ActualArrivalHour;   // The final timer (equals Expected if not delayed)
    
    public bool IsDelayed;
    public float DurabilityRoll;      
}

[Serializable]
public class ItemCounter
{
    public string BaseName;
    public int Count;
}

[Serializable]
public class VendorContainer
{
    public List<VendorData> AvailableVendors;
    public List<ActiveDeal> ActiveDeals;
    public List<ActiveOrder> ActiveOrders;
    public List<ItemCounter> LifetimeItemCounts; // Remembers IDs to increment
}