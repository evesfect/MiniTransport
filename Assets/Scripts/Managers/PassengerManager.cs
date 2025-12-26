using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[DefaultExecutionOrder(-45)] // Initialize after GridManager
public class PassengerManager : NetworkBehaviour
{
    public static PassengerManager Instance { get; private set; }

    // Key: BusStop.stopID, Value: List of passenger groups waiting there
    private Dictionary<string, List<WaitingPassengerGroup>> _stopWaitingData = new Dictionary<string, List<WaitingPassengerGroup>>();

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    // --- Server API ---

    public void AddPassengers(string stopID, int destinationTile, ushort count)
    {
        if (!IsServer) return;
        if (count == 0) return;

        if (!_stopWaitingData.ContainsKey(stopID))
            _stopWaitingData[stopID] = new List<WaitingPassengerGroup>();

        var groups = _stopWaitingData[stopID];
        
        // 1. Try to merge with existing group going to same destination
        bool found = false;
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].DestinationTileIndex == destinationTile)
            {
                var g = groups[i];
                // Prevent overflow
                int newCount = g.PassengerCount + count;
                g.PassengerCount = (ushort)Mathf.Clamp(newCount, 0, 65535);
                groups[i] = g;
                found = true;
                break;
            }
        }

        // 2. Create new group if unique destination
        if (!found)
        {
            groups.Add(new WaitingPassengerGroup 
            { 
                DestinationTileIndex = destinationTile, 
                PassengerCount = count 
            });
        }

        // 3. Sync to clients
        SyncStopPassengersClientRpc(stopID, groups.ToArray());
    }

    public void RemovePassengers(string stopID, int destinationTile, ushort count)
    {
        if (!IsServer) return;
        if (!_stopWaitingData.ContainsKey(stopID)) return;

        var groups = _stopWaitingData[stopID];
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i].DestinationTileIndex == destinationTile)
            {
                var g = groups[i];
                if (g.PassengerCount <= count)
                {
                    groups.RemoveAt(i);
                }
                else
                {
                    g.PassengerCount -= count;
                    groups[i] = g;
                }
                break;
            }
        }
        
        SyncStopPassengersClientRpc(stopID, groups.ToArray());
    }

    // --- Client Access ---

    public List<WaitingPassengerGroup> GetPassengersAtStop(string stopID)
    {
        if (_stopWaitingData.TryGetValue(stopID, out var list)) return list;
        return null;
    }
    
    public int GetTotalWaitingCount(string stopID)
    {
        if (_stopWaitingData.TryGetValue(stopID, out var list))
        {
            int total = 0;
            foreach(var g in list) total += g.PassengerCount;
            return total;
        }
        return 0;
    }

    // --- Network Sync ---

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncStopPassengersClientRpc(string stopID, WaitingPassengerGroup[] data)
    {
        if (!_stopWaitingData.ContainsKey(stopID))
            _stopWaitingData[stopID] = new List<WaitingPassengerGroup>();
        else
            _stopWaitingData[stopID].Clear();

        if (data != null && data.Length > 0)
        {
            _stopWaitingData[stopID].AddRange(data);
        }
    }
}