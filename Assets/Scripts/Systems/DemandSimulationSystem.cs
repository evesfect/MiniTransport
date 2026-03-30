using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class DemandSimulationSystem : GridSimulationSystem
{
    [Header("Generation Settings")]
    [Tooltip("Global multiplier for passenger spawn rates.")]
    public float globalSpawnRate = 1.0f; 
    
    [Header("Time Curves (X: Hour 0-24, Y: Multiplier 0-1)")]
    // OUT-DEMAND:
    public AnimationCurve resOutCurve;
    public AnimationCurve comOutCurve;
    public AnimationCurve indOutCurve;

    // IN-DEMAND:
    public AnimationCurve resInCurve;
    public AnimationCurve comInCurve;
    public AnimationCurve indInCurve;

    // Runtime State
    private float[] _destinationProbabilities; // The "Roulette Wheel" for destination selection
    private float _totalDestinationWeight;
    private bool _initialized = false;

    // Editor logic
    // This runs automatically when you add the component or Right Click -> Reset
    private void Reset()
    {
        SetupDefaultCurves();
    }

    private void SetupDefaultCurves()
    {
        // Creates a linear diagonal line from 0 to 1 over 24 hours.
        // This forces the Unity Inspector to frame the window correctly (0-24 X, 0-1 Y).
        resOutCurve = AnimationCurve.Linear(0, 0, 24, 1);
        comOutCurve = AnimationCurve.Linear(0, 0, 24, 1);
        indOutCurve = AnimationCurve.Linear(0, 0, 24, 1);

        resInCurve = AnimationCurve.Linear(0, 0, 24, 1);
        comInCurve = AnimationCurve.Linear(0, 0, 24, 1);
        indInCurve = AnimationCurve.Linear(0, 0, 24, 1);
    }
    // ---------------------------

    public override void Initialize(GridManager grid)
    {
        base.Initialize(grid);
        _destinationProbabilities = new float[grid.TotalTiles];
        
        // Safety check: If curves are somehow null/empty at runtime, set them up.
        if (resOutCurve == null || resOutCurve.length == 0) SetupDefaultCurves();
        
        _initialized = true;
    }

    public override void OnSimulationTick(float minutesPassed)
    {
        if (!IsServer || !_initialized) return;

        float currentTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
        
        // Calculate Time Factors for this tick
        float resOut = resOutCurve.Evaluate(currentTime);
        float comOut = comOutCurve.Evaluate(currentTime);
        float indOut = indOutCurve.Evaluate(currentTime);
        
        float resIn = resInCurve.Evaluate(currentTime);
        float comIn = comInCurve.Evaluate(currentTime);
        float indIn = indInCurve.Evaluate(currentTime);

        _totalDestinationWeight = 0f;
        int totalTiles = _grid.TotalTiles;

        // First Pass: Update Demand Values & Build Destination Weight Map
        for (int i = 0; i < totalTiles; i++)
        {
            TileData tile = _grid.GetTileData(i);
            
            // --- Out Demand Calculation
            // Formula: (Pop * ResRatio * Time) + (Jobs * (Com+Ind)Ratio * Time)
            // Normalized to 0-100 byte
            float rawOut = 0f;
            rawOut += (tile.Population * (tile.ResidentialRatio / 100f)) * resOut;
            rawOut += (tile.Jobs * (tile.CommercialRatio / 100f)) * comOut;
            rawOut += (tile.Jobs * (tile.IndustrialRatio / 100f)) * indOut;
            
            // Eco Class adjustment
            if (tile.EcoClass == EconomicClass.High) rawOut *= 0.75f;
            if (tile.EcoClass == EconomicClass.Low) rawOut *= 0.9f;

            byte newOutDemand = (byte)Mathf.Clamp(rawOut / 10f, 0, 100); // Scaling factor to fit byte

            // In Demand Calculation
            float rawIn = 0f;
            rawIn += (tile.Population * (tile.ResidentialRatio / 100f)) * resIn; // Returning home
            rawIn += (tile.Jobs * (tile.CommercialRatio / 100f)) * comIn;        // Going to shop/work
            rawIn += (tile.Jobs * (tile.IndustrialRatio / 100f)) * indIn;        // Going to work

            byte newInDemand = (byte)Mathf.Clamp(rawIn / 10f, 0, 100);

            // Update Grid Data if Changed
            if (tile.OutDemand != newOutDemand || tile.InDemand != newInDemand)
            {
                tile.OutDemand = newOutDemand;
                tile.InDemand = newInDemand;
                
                // Schedule update for visuals (low priority mask)
                _grid.ScheduleTileUpdate(i, tile, TileUpdateFlags.DemandValues);
            }

            // Cache Destination Probability
            // InDemand as the weight. 
            _destinationProbabilities[i] = rawIn; 
            _totalDestinationWeight += rawIn;
        }

        // Second Pass: Spawn Passengers
        // Iterate only tiles that have stops to save performance (to do) 
        // Currently iterates all, checks stops.
        for (int i = 0; i < totalTiles; i++)
        {
            TileData tile = _grid.GetTileData(i);
            if (tile.OutDemand < 1) continue;

            List<BusStop> stops = _grid.GetStopsInTile(i);
            if (stops == null || stops.Count == 0) continue;

            // Calculate how many people spawn this tick
            // OutDemand (0-100) is "People per Hour" roughly? 
            // minutesPassed is e.g. 15.
            float spawnChance = tile.OutDemand * globalSpawnRate * (minutesPassed / 60f);
            
            int spawnCount = Mathf.FloorToInt(spawnChance);
            if (UnityEngine.Random.value < (spawnChance - spawnCount)) spawnCount++;

            if (spawnCount > 0)
            {
                DistributePassengersToStops(stops, spawnCount, i);
            }
        }
    }

    private void DistributePassengersToStops(List<BusStop> stops, int count, int sourceIndex)
    {
        // Distribute evenly or random among stops in the tile
        for (int k = 0; k < count; k++)
        {
            BusStop randomStop = stops[Random.Range(0, stops.Count)];
            int destIndex = PickWeightedDestination();
            
            // Don't go to the same tile we are currently in
            if (destIndex == sourceIndex) continue;

            // Dont try to go to a tile that doesnt have a bus stop
            if (GridManager.Instance.GetStopsInTile(destIndex) == null) continue;

            PassengerManager.Instance.AddPassengers(randomStop.stopID, destIndex, 1);
        }
    }

    private int PickWeightedDestination()
    {
        if (_totalDestinationWeight <= 0) return Random.Range(0, _grid.TotalTiles);

        float rnd = Random.Range(0, _totalDestinationWeight);
        float currentSum = 0;

        for (int i = 0; i < _destinationProbabilities.Length; i++)
        {
            currentSum += _destinationProbabilities[i];
            if (rnd <= currentSum) return i;
        }
        return 0;
    }
}