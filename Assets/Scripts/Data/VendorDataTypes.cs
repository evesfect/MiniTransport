using System;
using System.Collections.Generic;
using UnityEngine;

public enum BusPartCategory
{
    None,
    Engine,     // Engine blocks, pistons
    Tires,      // Wheels, axels
    Chassis,    // Body, frame, doors
    Electronics // Sensors, dashboard
}

[Serializable]
public class VendorData
{
    public string VendorID;
    public string Name;
    public string Description;

    // --- Stats ---
    [Range(0, 100)]
    public float ReliabilityScore; // 0 = Always late, 100 = Always on time
    
    [Range(0, 5)]
    public int LoyaltyLevel;       // Higher level = Better prices/reliability
    public float CurrentXP;        // XP to next loyalty level

    [Tooltip("Base price multiplier. 1.0 = Standard, 0.8 = Cheap")]
    public float PriceMultiplier; 

    // --- Specialties ---
    // Vendors might be better at specific things (Visual flair only for now, or logic later)
    public BusPartCategory Specialty; 
}

[Serializable]
public class ActiveDeal
{
    public BusPartCategory Category; // E.g., This is our "Engine" provider
    public string VendorID;          // Who is the provider?
    public string StartDate;         // For tracking duration
}

[Serializable]
public class VendorContainer
{
    public List<VendorData> AllVendors;
    public List<ActiveDeal> ActiveDeals;
}