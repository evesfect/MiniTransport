using UnityEngine;
using System;

[DefaultExecutionOrder(-60)]
public class SimulationTimeManager : MonoBehaviour
{
    public static SimulationTimeManager Instance { get; private set; }

    [Header("Time Settings")]
    public float baseMinutesPerSecond = 5.0f;
    [Range(0f, 100f)] public float timeMultiplier = 1.0f;

    [Header("Current Status")]
    [SerializeField] private int _currentDay = 1;
    [SerializeField] [Range(0f, 24f)] private float _currentTimeOfDay = 6.0f;

    public event Action OnHourChanged;
    public event Action OnMinuteChanged;
    public event Action OnDayChanged;

    public float CurrentTimeOfDay => _currentTimeOfDay;
    public int CurrentDay => _currentDay;

    private int _lastHourCheck = -1;
    private int _lastMinuteCheck = -1;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        _lastHourCheck = Mathf.FloorToInt(_currentTimeOfDay);
        _lastMinuteCheck = Mathf.FloorToInt(_currentTimeOfDay * 60); // Init
    }

    void Update()
    {
        float deltaGameHours = (baseMinutesPerSecond * timeMultiplier * Time.deltaTime) / 60f;
        AdvanceTime(deltaGameHours);
    }

    private void AdvanceTime(float hoursToAdd)
    {
        _currentTimeOfDay += hoursToAdd;

        // Minute Check
        int currentMinuteTotal = Mathf.FloorToInt(_currentTimeOfDay * 60);
        if (currentMinuteTotal != _lastMinuteCheck)
        {
            _lastMinuteCheck = currentMinuteTotal;
            OnMinuteChanged?.Invoke(); // Fire every game-minute
        }

        // Hour Check
        int currentHourInt = Mathf.FloorToInt(_currentTimeOfDay);
        if (currentHourInt != _lastHourCheck)
        {
            _lastHourCheck = currentHourInt;
            OnHourChanged?.Invoke();
        }

        // Day Check
        if (_currentTimeOfDay >= 24.0f)
        {
            _currentTimeOfDay -= 24.0f;
            _currentDay++;
            
            // Reset trackers for the new day to prevent syncing issues
            _lastHourCheck = -1;
            _lastMinuteCheck = -1;
            
            OnDayChanged?.Invoke();
            Debug.Log($"Day Changed! Now Day: {_currentDay}");
        }
    }

    public string GetTimeString()
    {
        int hours = Mathf.FloorToInt(_currentTimeOfDay);
        int minutes = Mathf.FloorToInt((_currentTimeOfDay - hours) * 60);
        return $"{hours:00}:{minutes:00}";
    }

    public void SetMultiplier(float multiplier) => timeMultiplier = multiplier;
}