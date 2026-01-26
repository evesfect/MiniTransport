using Unity.Netcode;
using UnityEngine;
using System;

[System.Serializable]
public struct WaitingPassengerGroup : INetworkSerializable, IEquatable<WaitingPassengerGroup>
{
    public int DestinationTileIndex; // The specific tile they want to go to
    public ushort PassengerCount;    // How many people are waiting for this destination

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref DestinationTileIndex);
        serializer.SerializeValue(ref PassengerCount);
    }

    public bool Equals(WaitingPassengerGroup other)
    {
        return DestinationTileIndex == other.DestinationTileIndex && PassengerCount == other.PassengerCount;
    }
}