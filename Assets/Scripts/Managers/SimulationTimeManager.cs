using UnityEngine;
using Unity.Netcode;
using System;
using Unity.VisualScripting;

[DefaultExecutionOrder(-60)]
public class SimulationTimeManager : NetworkBehaviour
{
    public static SimulationTimeManager Instance { get; private set; }

    [Header("Time Settings")]
    public float baseMinutesPerSecond = 5.0f;
    [Range(0f, 100f)] public float timeMultiplier = 1.0f;
    [SerializeField] private float _timeOfDayStart = 6.0f;

    private readonly NetworkVariable<int> _netDay = new NetworkVariable<int>(1);
    private readonly NetworkVariable<float> _netTimeOfDay = new NetworkVariable<float>(
        6.0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private readonly NetworkVariable<float> _netTimeMultiplier = new NetworkVariable<float>(
        1.0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action OnHourChanged;
    public event Action OnMinuteChanged;
    public event Action OnDayChanged;

    public float CurrentTimeOfDay => _netTimeOfDay.Value;
    public int CurrentDay => _netDay.Value;
    public float TimeMultiplier => _netTimeMultiplier.Value;

    private int _lastDayCheck = -1;
    private int _lastHourCheck = -1;
    private int _lastMinuteCheck = -1;

    private float _clientVisualTime;
    private float _lastServerTime;
    private float _timeSinceLastSync;
    private bool IsDedicatedServer => IsServer && !IsClient;

    public void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; } //Singleton
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _netTimeOfDay.Value = _timeOfDayStart;
            _netDay.Value = 1;
        }
        _netTimeOfDay.OnValueChanged += OnServerTimeChanged;
    }


    void Update()
    {
        if (!IsSpawned){ return; }
        if (IsServer)
        {
            float deltaGameHours = (baseMinutesPerSecond * _netTimeMultiplier.Value * Time.deltaTime) / 60f;
            AdvanceTime(deltaGameHours);
        }
        if (IsClient && !IsDedicatedServer)
        {
            _timeSinceLastSync += Time.deltaTime;
            if (TimeMultiplier > 0f)
            {
                float predictedAdvance = (baseMinutesPerSecond * TimeMultiplier * _timeSinceLastSync) / 60f;
            _clientVisualTime = _lastServerTime + predictedAdvance;
            }
            else
            {
                _clientVisualTime = _lastServerTime;
            }
            
        }

        CheckEvents(_netTimeOfDay.Value);
            
    }

    private void CheckEvents(float currentTime)
    {
        // Day Check
        int currentDay = _netDay.Value;
        if(currentDay != _lastDayCheck)
        {
            _lastDayCheck = currentDay;
            OnDayChanged?.Invoke();
        }
        
        // Minute Check
        int currentMinuteTotal = Mathf.FloorToInt(currentTime * 60);
        if (currentMinuteTotal != _lastMinuteCheck)
        {
            _lastMinuteCheck = currentMinuteTotal;
            OnMinuteChanged?.Invoke(); 
        }

        // Hour Check
        int currentHourInt = Mathf.FloorToInt(currentTime);
        if (currentHourInt != _lastHourCheck)
        {
            _lastHourCheck = currentHourInt;
            OnHourChanged?.Invoke();
        }

    }

    private void AdvanceTime(float hoursToAdd)
    {
        float newTime = _netTimeOfDay.Value + hoursToAdd;

        if (newTime >= 24.0f)
        {
            newTime -= 24.0f;
            _netDay.Value++;
        }

        _netTimeOfDay.Value = newTime;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestTimeMultiplierRpc(float newMultiplier)
    {
        _netTimeMultiplier.Value = Mathf.Clamp(newMultiplier, 0f, 100f);   
    }

    private void OnServerTimeChanged(float oldValue, float newValue)
    {
        _lastServerTime = newValue;
        _clientVisualTime = newValue; // Seed the client visual time
        _timeSinceLastSync = 0f;
    }


    public string GetTimeString(float t)
    {
        int hours = Mathf.FloorToInt(t);
        int minutes = Mathf.FloorToInt((t - hours) * 60);
        return $"{hours:00}:{minutes:00}";
    }

    void OnGUI()
    {
        const float width = 220f;
        const float height = 200f;
        const float padding = 10f;

        Rect rect = new Rect(
            Screen.width - width - padding,
            padding,
            width,
            height
        );

        GUI.Box(rect, "Time Debug");

        GUILayout.BeginArea(rect);
        GUILayout.Space(20);

        GUILayout.Label($"Day: {_netDay.Value}");
        GUILayout.Label($"Network Time (float): {_netTimeOfDay.Value:F4}");
        GUILayout.Label($"Network Time: {GetTimeString(_netTimeOfDay.Value)}");
        GUILayout.Label($"Client Visual Time: {GetTimeString(_clientVisualTime)}");
        GUILayout.Label($"Multiplier: {_netTimeMultiplier.Value}x");

        GUILayout.Space(8);
        
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Pause"))
            RequestTimeMultiplierRpc(0f);

        if (GUILayout.Button("1x"))
            RequestTimeMultiplierRpc(1f);

        if (GUILayout.Button("3x"))
            RequestTimeMultiplierRpc(3f);

        if (GUILayout.Button("10x"))
            RequestTimeMultiplierRpc(10f);

        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

}