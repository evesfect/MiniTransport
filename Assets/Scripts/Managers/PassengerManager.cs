using System;
using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[DefaultExecutionOrder(-45)] // Initialize after GridManager
public class PassengerManager : NetworkBehaviour
{
    public static PassengerManager Instance { get; private set; }

    // Key: BusStop.stopID, Value: List of passenger groups waiting there
    private Dictionary<string, List<WaitingPassengerGroup>> _stopWaitingData = new Dictionary<string, List<WaitingPassengerGroup>>();
    
    [Header("Patience Settings")]
    [Tooltip("How many game-hours a passenger waits before leaving angry.")]
    public float maxWaitTimeHours = 2.5f;

    // --- KPI collection hooks (server-side; consumed by KPIManager) ---
    public event Action<float, int> OnPassengersServed;   // waitHours, count (boarded before timeout)
    public event Action<int> OnPassengersTimedOut;        // count (gave up waiting)
    // Number Of Transfers is detected in BusDriver when a passenger alights at a connecting
    // stop, counted in CompanyManager.TransferTripCount, and surfaced to the KPI report via
    // CompanyManager.OnTransferRecorded.

    // Optimization: Only check timeouts every X frames to save performance
    private int _tickCounter = 0;
    private const int CHECK_INTERVAL_FRAMES = 600; // Approx every 10-20 seconds depending on FPS
    
    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    private void Update()
    {
        if (!IsServer) return;

        // Periodically check for unhappy passengers
        _tickCounter++;
        if (_tickCounter >= CHECK_INTERVAL_FRAMES)
        {
            _tickCounter = 0;
            CheckPassengerTimeouts();
        }
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
                PassengerCount = count,
                SpawnTime = SimulationTimeManager.Instance.CurrentTimeOfDay
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
                int boarded = Mathf.Min(count, g.PassengerCount);

                // KPI: these passengers boarded a bus, so record how long they waited.
                if (boarded > 0 && SimulationTimeManager.Instance != null)
                {
                    float waitHours = SimulationTimeManager.Instance.CurrentTimeOfDay - g.SpawnTime;
                    if (waitHours < 0) waitHours += 24f; // day wrap-around
                    OnPassengersServed?.Invoke(waitHours, boarded);
                }

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

    private void CheckPassengerTimeouts()
    {
        
        if (CompanyManager.Instance == null || SimulationTimeManager.Instance == null) return;

        float currentTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
        List<string> stopsToUpdate = new List<string>();

        // Iterate over every stop in the game
        var stopIDs = new List<string>(_stopWaitingData.Keys);

        foreach (string stopID in stopIDs)
        {
            var groups = _stopWaitingData[stopID];
            bool changed = false;

            // Iterate backwards so we can remove items safely
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                // Calculate how long they have been waiting
                float arrivalTime = groups[i].SpawnTime;
                float waitDuration = currentTime - arrivalTime;

                // Handle Day Wrap-around (e.g., Arrived at 23:00, Current is 01:00)
                if (waitDuration < 0)
                {
                    waitDuration += 24f;
                }

                if (waitDuration > maxWaitTimeHours)
                {
                    // TIMEOUT! They leave angry.
                    int leavingCount = groups[i].PassengerCount;

                    
                    // Softened: 0.025 (was 0.1) so a wave of give-ups no longer tanks satisfaction.
                    // Net effect with the default penalty field ≈ -0.05 satisfaction per passenger who leaves.
                    float penalty = -(leavingCount * 0.025f * CompanyManager.Instance.satisfactionPenaltyPertimeout);

                    CompanyManager.Instance.ModifySatisfaction(penalty, $"Passengers gave up at Stop {stopID}");

                    OnPassengersTimedOut?.Invoke(leavingCount); // KPI: missed passengers

                    
                    //Debug.Log($"[PassengerManager] {leavingCount} passengers gave up at Stop {stopID}. Penalty: {penalty}");

                    groups.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
            {
                stopsToUpdate.Add(stopID);
            }
        }

        // Sync only the stops that changed
        foreach (string stopID in stopsToUpdate)
        {
            SyncStopPassengersClientRpc(stopID, _stopWaitingData[stopID].ToArray());
        }
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