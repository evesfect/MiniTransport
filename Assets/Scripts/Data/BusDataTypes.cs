using System;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

[Serializable]
public class BusSchedule
{
    public string RouteID;
    public float StartTime = 8.0f;
    public float EndTime = 17.0f;
    public float TurnaroundWait = 10f;
}

[Serializable]
public class BusData
{
    public string BusID;
    public string AssignedDepotID;
    public BusSchedule Schedule;
    public float Durability = 100f;
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
               Mathf.Approximately(BreakdownStopDistance, other.BreakdownStopDistance);
    }
}