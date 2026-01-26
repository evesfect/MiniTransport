using Unity.Netcode;
using UnityEngine;

public enum EconomicClass : byte
{
    Low = 0,
    Medium = 1,
    High = 2
}

// Main Data Struct for a Tile
[System.Serializable]
public struct TileData : INetworkSerializable
{
    public byte Traffic;           // 0-100
    public ushort Population;      // 0-65535
    public ushort Jobs;
    public byte InDemand;            // 0-255
    public byte OutDemand; // 0-255
    
    // Ratios (Sums to 100)
    public byte ResidentialRatio;
    public byte CommercialRatio;
    public byte IndustrialRatio;
    
    public EconomicClass EcoClass;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Traffic);
        serializer.SerializeValue(ref Population);
        serializer.SerializeValue(ref Jobs);
        serializer.SerializeValue(ref OutDemand);
        serializer.SerializeValue(ref InDemand);
        serializer.SerializeValue(ref ResidentialRatio);
        serializer.SerializeValue(ref CommercialRatio);
        serializer.SerializeValue(ref IndustrialRatio);
        serializer.SerializeValue(ref EcoClass);
    }
}

// Mask for future partial updates
[System.Flags]
public enum TileUpdateFlags : byte
{
    None = 0,
    Traffic = 1 << 0,
    Population = 1 << 1,
    Jobs = 1 << 2, 
    Ratios = 1 << 3,
    Economy = 1 << 4,
    DemandValues = 1 << 5,
    All = 255
}

// Packet for scheduled updates
[System.Serializable]
public struct TileUpdatePacket : INetworkSerializable
{
    public int TileIndex;
    public TileData Data;
    public TileUpdateFlags Mask;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref TileIndex);
        serializer.SerializeValue(ref Mask);

        if ((Mask & TileUpdateFlags.Traffic) != 0) serializer.SerializeValue(ref Data.Traffic);
        if ((Mask & TileUpdateFlags.Population) != 0) serializer.SerializeValue(ref Data.Population);
        if ((Mask & TileUpdateFlags.Jobs) != 0) serializer.SerializeValue(ref Data.Jobs);
        
        if ((Mask & TileUpdateFlags.DemandValues) != 0) 
        {
            serializer.SerializeValue(ref Data.OutDemand);
            serializer.SerializeValue(ref Data.InDemand);
        }
        
        if ((Mask & TileUpdateFlags.Ratios) != 0)
        {
            serializer.SerializeValue(ref Data.ResidentialRatio);
            serializer.SerializeValue(ref Data.CommercialRatio);
            serializer.SerializeValue(ref Data.IndustrialRatio);
        }
        
        if ((Mask & TileUpdateFlags.Economy) != 0) serializer.SerializeValue(ref Data.EcoClass);
    }
    
}

public struct PendingGridUpdate
{
    public float ExecutionTime; // The Game Time (VisualTime) when this applies
    public TileUpdatePacket Packet;
}