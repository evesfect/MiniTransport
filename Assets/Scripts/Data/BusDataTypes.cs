using System;
using UnityEngine;

// Defines the static configuration of a bus's workday
[Serializable]
public class BusSchedule
{
    public string RouteID;          // The route to follow
    public float StartTime = 8.0f;  // When to leave depot (Hours 0-24)
    public float EndTime = 17.0f;   // When to stop new loops (Hours 0-24)
    public float TurnaroundWait = 10f; // Minutes to wait at terminal stops
}

// Defines the runtime state and identity of a specific bus
[Serializable]
public class DepotBusEntry
{
    public string BusID;
    public string AssignedDepotID;
    public BusSchedule Schedule;   // The assigned schedule
    
    // Runtime State (Not necessarily saved to JSON if we restart days)
    [NonSerialized] public GameObject ActiveBusInstance; 
    public BusState CurrentState = BusState.InDepot;
}

public enum BusState
{
    InDepot,
    OnRoute,
    CompletingTrip // State when past EndTime but finishing the loop
}