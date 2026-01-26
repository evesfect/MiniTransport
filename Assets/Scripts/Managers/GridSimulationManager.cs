using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[DefaultExecutionOrder(-40)] // Runs after GridManager (-50)
public class GridSimulationManager : NetworkBehaviour
{
    [Header("Timing")]
    [Tooltip("How often (in Game Minutes) the simulation calculates changes.")]
    public float simulationStepMinutes = 15f; 

    [Header("Systems")]
    [SerializeField] private List<GridSimulationSystem> _activeSystems = new List<GridSimulationSystem>();

    private float _lastSimGameTime;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _lastSimGameTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
            InitializeSystems();
        }
    }

    private void InitializeSystems()
    {
        // Auto-detect systems attached to this GameObject
        if (_activeSystems.Count == 0)
        {
            var systems = GetComponents<GridSimulationSystem>();
            _activeSystems.AddRange(systems);
        }

        foreach (var sys in _activeSystems)
        {
            sys.Initialize(GridManager.Instance);
            Debug.Log($"Sim Manager: Initialized system {sys.GetType().Name}");
        }
    }

    private void Update()
    {
        if (!IsServer) return;
        if (SimulationTimeManager.Instance == null) return;

        float currentGameTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
        
        float timeDelta = currentGameTime - _lastSimGameTime;
        if (timeDelta < 0) timeDelta += 24f; 

        float minutesPassed = timeDelta * 60f;

        if (minutesPassed >= simulationStepMinutes)
        {
            // Run the logic tick for all systems
            foreach (var sys in _activeSystems)
            {
                if (sys.enabled) sys.OnSimulationTick(minutesPassed);
            }

            _lastSimGameTime = currentGameTime;
        }
    }
}