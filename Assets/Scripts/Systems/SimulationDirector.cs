using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public enum WeatherState { Clear, Rain, Snow, Storm }

public class SimulationDirector : GridSimulationSystem
{
    public static SimulationDirector Instance { get; private set; }

    [Header("Feature 1: Global Growth (Difficulty)")]
    [Tooltip("Global population added to EVERY tile per game hour.")]
    public float globalPopGrowthPerHour = 2.0f;
    [Tooltip("Global jobs added to EVERY tile per game hour.")]
    public float globalJobGrowthPerHour = 2.0f;
    [Tooltip("Increases the base passenger spawn rate per game hour to bypass the 100 demand cap.")]
    public float spawnRateGrowthPerHour = 0.01f;
    
    // Read by DemandSimulationSystem
    public float CurrentSpawnRateMultiplier { get; private set; } = 1.0f; 

    [Header("Feature 2: Player Impact")]
    [Tooltip("Extra population added to a tile per game hour IF it has a bus stop.")]
    public float playerImpactGrowthBonus = 5.0f;
    [Tooltip("Extra jobs added to a tile per game hour IF it has a bus stop.")]
    public float playerImpactJobGrowthBonus = 5.0f;

    [Header("Feature 3: Weather System")]
    public WeatherState currentWeather = WeatherState.Clear;
    public float weatherMinDurationHours = 2f;
    public float weatherMaxDurationHours = 6f;
    [Tooltip("Traffic points removed per hour when weather clears")]
    public float weatherClearFadeOutSpeed = 15f; 
    
    private float _weatherTimer;
    private float _currentWeatherTrafficPenalty;
    private string _activeWeatherBatchId = ""; // [NEW] Tracks the notification ID

    [Header("Feature 4: Special Events")]
    public int activeEventTileIndex = -1; 
    public float eventCooldownHours = 24f;
    private float _eventCooldownTimer;
    private string _activeEventBatchId = ""; // [NEW] Tracks the notification ID
    
    [Tooltip("Tracks hours. -3 = Prep, 0-3 = Game, 3-5 = Aftermath")]
    public float currentEventTime = 0f;

    // The Curves! (X = Hours -3 to +5, Y = Multiplier/Bonus)
    public AnimationCurve eventInDemandCurve;
    public AnimationCurve eventOutDemandCurve;
    public AnimationCurve eventTrafficCurve;

    // Internal trackers for fractional growth
    private float[] _fractionalPopulationAccumulator;
    private float[] _fractionalJobAccumulator;

    private void Reset()
    {
        // Auto-sets up the exact Match Day timeline
        eventInDemandCurve = new AnimationCurve(
            new Keyframe(-3f, 1f),    // Normal 3 hrs before
            new Keyframe(0f, 10f),    // Massive spike as game starts
            new Keyframe(3f, 8f),     // Still high during game (latecomers)
            new Keyframe(3.1f, 0f),   // Game ends, nobody goes IN
            new Keyframe(5f, 1f)      // Back to normal
        );

        eventOutDemandCurve = new AnimationCurve(
            new Keyframe(-3f, 1f),    
            new Keyframe(0f, 0.1f),   // Nobody leaves as game starts
            new Keyframe(3f, 0f),     // Nobody leaves during game
            new Keyframe(3.1f, 10f),  // Game ends! Massive spike OUT
            new Keyframe(5f, 1f)      // Slowly back to normal
        );

        eventTrafficCurve = new AnimationCurve(
            new Keyframe(-3f, 0f),
            new Keyframe(0f, 80f),    // Massive traffic jam getting there
            new Keyframe(1f, 0f),     // Traffic clears while people watch
            new Keyframe(3f, 0f),     
            new Keyframe(3.1f, 80f),  // Traffic jam leaving!
            new Keyframe(5f, 0f)      // Clears out
        );
    }

    public override void Initialize(GridManager grid)
    {
        base.Initialize(grid);
        Instance = this;
        _fractionalPopulationAccumulator = new float[grid.TotalTiles];
        _fractionalJobAccumulator = new float[grid.TotalTiles];
        _weatherTimer = Random.Range(weatherMinDurationHours, weatherMaxDurationHours);
        _eventCooldownTimer = eventCooldownHours;
        
        if (eventInDemandCurve == null || eventInDemandCurve.length == 0) Reset();
    }

    public override void OnSimulationTick(float minutesPassed)
    {
        if (!IsServer) return;
        float hoursPassed = minutesPassed / 60f;

        ProcessGrowth(hoursPassed);
        ProcessWeather(hoursPassed);
        ProcessSpecialEvents(hoursPassed);
        ApplyTrafficToGrid();
    }

    private void ProcessGrowth(float hoursPassed)
    {
        // Feature 1: Global Difficulty Increase
        CurrentSpawnRateMultiplier += spawnRateGrowthPerHour * hoursPassed;

        for (int i = 0; i < _grid.TotalTiles; i++)
        {
            float popGrowthThisTick = globalPopGrowthPerHour * hoursPassed;
            float jobGrowthThisTick = globalJobGrowthPerHour * hoursPassed;
            
            // Feature 2: Player Impact
            List<BusStop> stops = _grid.GetStopsInTile(i);
            if (stops != null && stops.Count > 0)
            {
                float localImpact = stops.Count * hoursPassed;
                popGrowthThisTick += playerImpactGrowthBonus * localImpact;
                jobGrowthThisTick += playerImpactJobGrowthBonus * localImpact;
            }

            _fractionalPopulationAccumulator[i] += popGrowthThisTick;
            _fractionalJobAccumulator[i] += jobGrowthThisTick;
            
            bool requiresUpdate = false;
            TileUpdateFlags flagsToUpdate = TileUpdateFlags.None;
            TileData tile = _grid.GetTileData(i);

            // Process Population Growth
            if (_fractionalPopulationAccumulator[i] >= 1f)
            {
                int newPop = Mathf.FloorToInt(_fractionalPopulationAccumulator[i]);
                tile.Population += (ushort)newPop;
                _fractionalPopulationAccumulator[i] -= newPop;
                flagsToUpdate |= TileUpdateFlags.Population;
                requiresUpdate = true;
            }

            // Process Job Growth
            if (_fractionalJobAccumulator[i] >= 1f)
            {
                int newJobs = Mathf.FloorToInt(_fractionalJobAccumulator[i]);
                tile.Jobs += (ushort)newJobs;
                _fractionalJobAccumulator[i] -= newJobs;
                flagsToUpdate |= TileUpdateFlags.Jobs;
                requiresUpdate = true;
            }

            // Sync the combined changes
            if (requiresUpdate)
            {
                _grid.ScheduleTileUpdate(i, tile, flagsToUpdate);
            }
        }
    }

    private void ProcessWeather(float hoursPassed)
    {
        _weatherTimer -= hoursPassed;
        
        if (_weatherTimer <= 0)
        {
            if (currentWeather != WeatherState.Clear)
            {
                currentWeather = WeatherState.Clear;
                _weatherTimer = Random.Range(12f, 24f); 
                Debug.Log("[Simulation Director] Weather cleared. Traffic will settle.");

                // [NEW] End the active weather notification
                if (!string.IsNullOrEmpty(_activeWeatherBatchId) && RequestManager.Instance != null)
                {
                    RequestManager.Instance.EndSystemEvent(_activeWeatherBatchId);
                    _activeWeatherBatchId = "";
                }
            }
            else
            {
                float roll = Random.value;
                if (roll < 0.5f) { currentWeather = WeatherState.Rain; _currentWeatherTrafficPenalty = 30f; }
                else if (roll < 0.8f) { currentWeather = WeatherState.Snow; _currentWeatherTrafficPenalty = 60f; }
                else { currentWeather = WeatherState.Storm; _currentWeatherTrafficPenalty = 90f; }
                
                _weatherTimer = Random.Range(weatherMinDurationHours, weatherMaxDurationHours);
                Debug.Log($"[Simulation Director] Sudden {currentWeather}! Traffic spike!");

                // [NEW] Broadcast the weather event
                if (RequestManager.Instance != null)
                {
                    _activeWeatherBatchId = System.Guid.NewGuid().ToString().Substring(0, 8);
                    RequestManager.Instance.BroadcastSystemEvent(_activeWeatherBatchId, $"Sudden {currentWeather} conditions have begun! Expect heavy traffic delays across the city.");
                }
            }
        }

        if (currentWeather == WeatherState.Clear && _currentWeatherTrafficPenalty > 0)
        {
            _currentWeatherTrafficPenalty -= weatherClearFadeOutSpeed * hoursPassed;
            if (_currentWeatherTrafficPenalty < 0) _currentWeatherTrafficPenalty = 0;
        }
    }

    private void ProcessSpecialEvents(float hoursPassed)
    {
        if (activeEventTileIndex == -1)
        {
            _eventCooldownTimer -= hoursPassed;
            if (_eventCooldownTimer <= 0)
            {
                _eventCooldownTimer = eventCooldownHours;
                if (Random.value > 0.3f) 
                {
                    activeEventTileIndex = Random.Range(0, _grid.TotalTiles);
                    currentEventTime = -3f;
                    Debug.Log($"[Simulation Director] Match Day announced at Tile {activeEventTileIndex}!");

                    // [NEW] Broadcast the Match Day event
                    if (RequestManager.Instance != null)
                    {
                        _activeEventBatchId = System.Guid.NewGuid().ToString().Substring(0, 8);
                        RequestManager.Instance.BroadcastSystemEvent(_activeEventBatchId, $"Match Day announced at Tile {activeEventTileIndex}! Massive demand and traffic spikes in the area.");
                    }
                }
            }
        }
        else
        {
            currentEventTime += hoursPassed;
            if (currentEventTime > 5f)
            {
                activeEventTileIndex = -1; 
                Debug.Log("[Simulation Director] Match Day aftermath cleared.");

                // [NEW] End the match day notification
                if (!string.IsNullOrEmpty(_activeEventBatchId) && RequestManager.Instance != null)
                {
                    RequestManager.Instance.EndSystemEvent(_activeEventBatchId);
                    _activeEventBatchId = "";
                }
            }
        }
    }

    private void ApplyTrafficToGrid()
    {
        for (int i = 0; i < _grid.TotalTiles; i++)
        {
            TileData tile = _grid.GetTileData(i);
            byte targetTraffic = (byte)Mathf.Clamp(_currentWeatherTrafficPenalty, 0, 100);
            
            if (activeEventTileIndex == i) 
            {
                float eventTraffic = eventTrafficCurve.Evaluate(currentEventTime);
                targetTraffic = (byte)Mathf.Clamp(targetTraffic + eventTraffic, 0, 100);
            }

            if (tile.Traffic != targetTraffic)
            {
                tile.Traffic = targetTraffic;
                _grid.ScheduleTileUpdate(i, tile, TileUpdateFlags.Traffic);
            }
        }
    }

    public void ApplyDemandModifiers(int tileIndex, ref float rawOutDemand, ref float rawInDemand)
    {
        if (currentWeather == WeatherState.Rain) { rawOutDemand *= 0.8f; rawInDemand *= 0.8f; }
        if (currentWeather == WeatherState.Snow) { rawOutDemand *= 0.5f; rawInDemand *= 0.5f; }
        if (currentWeather == WeatherState.Storm) { rawOutDemand *= 0.2f; rawInDemand *= 0.2f; }

        if (activeEventTileIndex != -1)
        {
            float inMult = eventInDemandCurve.Evaluate(currentEventTime);
            float outMult = eventOutDemandCurve.Evaluate(currentEventTime);

            if (tileIndex == activeEventTileIndex)
            {
                rawInDemand += (1000f * inMult); 
                rawOutDemand *= outMult;
            }
            else
            {
                rawInDemand *= outMult; 
                rawOutDemand *= inMult; 
            }
        }
    }

    public float GetDirectSpawnMultiplier(int tileIndex)
    {
        float mult = CurrentSpawnRateMultiplier;

        if (currentWeather == WeatherState.Rain) mult *= 0.7f;
        if (currentWeather == WeatherState.Snow) mult *= 0.4f;
        if (currentWeather == WeatherState.Storm) mult *= 0.1f;

        if (activeEventTileIndex != -1)
        {
            if (tileIndex == activeEventTileIndex) 
            {
                mult *= eventOutDemandCurve.Evaluate(currentEventTime); 
            }
            else 
            {
                mult *= eventInDemandCurve.Evaluate(currentEventTime); 
            }
        }
        
        return mult;
    }
}