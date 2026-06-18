using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public class BusSchedule
{
    public string RouteID;
    public float StartTime = 8.0f;
    public float EndTime = 17.0f;
    public float TurnaroundWait = 10f;
}

public enum BusPartType
{
    Engine = 0,
    Transmission = 1,
    Tires = 2,
    Body = 3,
    Interior = 4,
    None = 255 // For when not broken
}

[Serializable]
public class BusPartData
{
    public BusPartType PartType;
    public float Health = 100f;   // Current condition (0 = Breakdown)
    public float MaxLife = 100f;  // Maximum repairable condition (0 = Scrap)
    public string PendingReplacementItemID = "";

    public BusPartData(BusPartType type)
    {
        PartType = type;
        Health = 100f;
        MaxLife = 100f;
    }
}

[Serializable]
public class BusData
{
    public string BusID;
    public string AssignedDepotID;
    public BusSchedule Schedule;
    public bool PendingSale = false;
    public List<BusPartData> Parts = new List<BusPartData>();
    public ushort Capacity;

    public void InitializeParts()
    {
        if (Parts.Count == 0)
        {
            Parts.Add(new BusPartData(BusPartType.Engine));
            Parts.Add(new BusPartData(BusPartType.Transmission));
            Parts.Add(new BusPartData(BusPartType.Tires));
            Parts.Add(new BusPartData(BusPartType.Body));
            Parts.Add(new BusPartData(BusPartType.Interior));
        }
    }

    public float GetAverageHealth()
    {
        if (Parts == null || Parts.Count == 0) return 0f;
        float sum = 0;
        foreach (var p in Parts) sum += p.Health;
        return sum / Parts.Count;
    }
}

// Per-bus live runtime status mirrored from the server to every client (FleetManager keeps the
// authoritative list). Lets client UI show whether a bus is actually out driving, returning, or
// broken down — the server-only spawned-instance map can't be read on clients.
public struct BusRuntimeStatus : INetworkSerializable, IEquatable<BusRuntimeStatus>
{
    public FixedString32Bytes BusID;
    public bool IsRecalled;   // player asked it to return / be towed
    public bool IsBroken;     // broken down on the road

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref BusID);
        serializer.SerializeValue(ref IsRecalled);
        serializer.SerializeValue(ref IsBroken);
    }

    public bool Equals(BusRuntimeStatus other) =>
        BusID.Equals(other.BusID) && IsRecalled == other.IsRecalled && IsBroken == other.IsBroken;
}

public struct BusNetworkState : INetworkSerializable, IEquatable<BusNetworkState>
{
    public FixedString32Bytes CurrentRouteID;
    public FixedString32Bytes PreviousStopID;
    public FixedString32Bytes TargetStopID;
    public float DepartureTime;
    public bool IsReverseDirection;
    public bool IsInService;
    public bool IsBrokenDown;
    public float BreakdownStopDistance;
    public ushort PassengerCount;
    public BusPartType BreakdownReason;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref CurrentRouteID);
        serializer.SerializeValue(ref PreviousStopID);
        serializer.SerializeValue(ref TargetStopID);
        serializer.SerializeValue(ref DepartureTime);
        serializer.SerializeValue(ref IsReverseDirection);
        serializer.SerializeValue(ref IsInService);
        serializer.SerializeValue(ref IsBrokenDown);
        serializer.SerializeValue(ref BreakdownStopDistance);
        serializer.SerializeValue(ref PassengerCount);
        serializer.SerializeValue(ref BreakdownReason);
    }

    public bool Equals(BusNetworkState other)
    {
        return CurrentRouteID == other.CurrentRouteID &&
               PreviousStopID == other.PreviousStopID &&
               TargetStopID == other.TargetStopID &&
               DepartureTime == other.DepartureTime &&
               IsReverseDirection == other.IsReverseDirection &&
               IsInService == other.IsInService &&
               IsBrokenDown == other.IsBrokenDown &&
               Mathf.Approximately(BreakdownStopDistance, other.BreakdownStopDistance) &&
               PassengerCount == other.PassengerCount && 
               BreakdownReason == other.BreakdownReason;
    }
}