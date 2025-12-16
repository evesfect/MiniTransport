using System;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

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
    public BusSchedule Schedule;
    
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

public struct BusNetworkState : INetworkSerializable, IEquatable<BusNetworkState>
{
    public FixedString32Bytes CurrentRouteID;
    public FixedString32Bytes PreviousStopID;
    public FixedString32Bytes TargetStopID;

    public float DepartureTime; // Server sim time
    
    public bool IsReverseDirection;
    public bool IsInService;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref CurrentRouteID);
        serializer.SerializeValue(ref PreviousStopID);
        serializer.SerializeValue(ref TargetStopID);
        serializer.SerializeValue(ref DepartureTime);
        serializer.SerializeValue(ref IsReverseDirection);
        serializer.SerializeValue(ref IsInService);
    }
    public bool Equals(BusNetworkState other)
    {
        return CurrentRouteID == other.CurrentRouteID &&
               PreviousStopID == other.PreviousStopID &&
               TargetStopID == other.TargetStopID &&
               DepartureTime == other.DepartureTime &&
               IsReverseDirection == other.IsReverseDirection &&
               IsInService == other.IsInService;
    }

}