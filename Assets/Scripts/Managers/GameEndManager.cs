using System;
using Unity.Netcode;
using UnityEngine;

public enum GameEndReason : byte
{
    None = 0,
    Bankrupt = 1,
    TimeLimit = 2
}

/// <summary>
/// Server-authoritative game-over controller. Monitors the two end conditions (bankruptcy and
/// time limit), and when either fires it freezes + locks the simulation clock and pushes the
/// final KPI report snapshots. The result is mirrored to every peer via NetworkVariables so the
/// client GameOverScreen can reveal the reports.
/// </summary>
[DefaultExecutionOrder(-30)] // after the managers it reads from (Company/Time/KPI)
public class GameEndManager : NetworkBehaviour
{
    public static GameEndManager Instance { get; private set; }

    [Header("Bankruptcy")]
    [Tooltip("End the game when the balance falls below CompanyManager.bankruptcyThreshold.")]
    public bool enableBankruptcy = true;

    [Header("Time Limit")]
    public bool enableTimeLimit = true;
    [Tooltip("Game ends as the day AFTER this begins (e.g. 30 -> ends when day 31 starts).")]
    public int gameLengthDays = 30;

    private readonly NetworkVariable<bool> _gameOver = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<GameEndReason> _reason = new NetworkVariable<GameEndReason>(GameEndReason.None);

    public bool IsGameOver => _gameOver.Value;
    public GameEndReason Reason => _reason.Value;

    /// <summary>Fired on every peer when the game ends (carries the reason).</summary>
    public event Action<GameEndReason> OnGameEnded;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        _gameOver.OnValueChanged += HandleGameOverChanged;

        // Late joiner: if the game is already over, surface it immediately.
        if (_gameOver.Value) OnGameEnded?.Invoke(_reason.Value);

        if (!IsServer) return;

        if (CompanyManager.Instance != null)
            CompanyManager.Instance.OnBalanceChanged += OnBalanceChanged;
        if (SimulationTimeManager.Instance != null)
            SimulationTimeManager.Instance.OnDayChanged += OnDayChanged;
    }

    public override void OnNetworkDespawn()
    {
        _gameOver.OnValueChanged -= HandleGameOverChanged;

        if (!IsServer) return;

        if (CompanyManager.Instance != null)
            CompanyManager.Instance.OnBalanceChanged -= OnBalanceChanged;
        if (SimulationTimeManager.Instance != null)
            SimulationTimeManager.Instance.OnDayChanged -= OnDayChanged;
    }

    // --- Server-side condition monitoring ---

    private void OnBalanceChanged(float newBalance)
    {
        if (!IsServer || _gameOver.Value || !enableBankruptcy) return;
        if (CompanyManager.Instance == null) return;

        // Falls below the configured max debt -> bankrupt. Player purchases are already floored at
        // this threshold, so this only trips via unavoidable obligations (payroll, upkeep).
        if (newBalance < CompanyManager.Instance.bankruptcyThreshold)
            EndGame(GameEndReason.Bankrupt);
    }

    private void OnDayChanged()
    {
        if (!IsServer || _gameOver.Value || !enableTimeLimit) return;

        if (SimulationTimeManager.Instance != null &&
            SimulationTimeManager.Instance.CurrentDay > gameLengthDays)
            EndGame(GameEndReason.TimeLimit);
    }

    // Server-only. Ends the game exactly once.
    private void EndGame(GameEndReason reason)
    {
        if (!IsServer || _gameOver.Value) return;

        _reason.Value = reason;   // set reason before flipping the flag so it is in sync on clients
        _gameOver.Value = true;

        if (SimulationTimeManager.Instance != null)
            SimulationTimeManager.Instance.LockTime(); // freeze the sim and block speed changes

        if (KPIManager.Instance != null)
            KPIManager.Instance.FinalizeReports();      // push final report snapshots to subscribers

        Debug.Log($"[GameEnd] Game over: {reason}");
    }

    private void HandleGameOverChanged(bool wasOver, bool isOver)
    {
        if (isOver) OnGameEnded?.Invoke(_reason.Value);
    }
}
