using System;
using Unity.Netcode;
using UnityEngine;

public enum PlayerRole
{
    Operations,
    Maintenance,
    HR,
    Finance,
    Vendor
}

/// <summary>
/// Lightweight, server-authoritative player -> role/domain assignment.
/// Each of the (up to 5) players owns one domain; their per-player end-of-game report is simply
/// that domain's KPIs (no per-action attribution). Roles are assigned round-robin on connect for
/// now; a lobby selection screen can replace AssignRole later without touching the report pipeline.
/// </summary>
[DefaultExecutionOrder(-40)]
public class PlayerRoleManager : NetworkBehaviour
{
    public static PlayerRoleManager Instance { get; private set; }

    private NetworkList<RoleAssignment> _assignments;

    // Order players are handed domains as they join.
    private static readonly PlayerRole[] RoleCycle =
    {
        PlayerRole.Operations,
        PlayerRole.Maintenance,
        PlayerRole.HR,
        PlayerRole.Finance,
        PlayerRole.Vendor
    };

    /// <summary>Raised on every peer when assignments change (e.g. a player joined).</summary>
    public event Action OnRolesChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // NetworkList must exist before OnNetworkSpawn.
        _assignments = new NetworkList<RoleAssignment>();
    }

    public override void OnNetworkSpawn()
    {
        _assignments.OnListChanged += OnAssignmentsChanged;

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += AssignRole;

            // Cover anyone already connected (notably the host's own client).
            foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
                AssignRole(id);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_assignments != null) _assignments.OnListChanged -= OnAssignmentsChanged;

        if (IsServer && NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= AssignRole;
    }

    private void OnAssignmentsChanged(NetworkListEvent<RoleAssignment> _) => OnRolesChanged?.Invoke();

    private void AssignRole(ulong clientId)
    {
        if (!IsServer) return;

        // Already assigned? Leave it.
        foreach (var a in _assignments)
            if (a.ClientId == clientId) return;

        PlayerRole role = RoleCycle[_assignments.Count % RoleCycle.Length];
        _assignments.Add(new RoleAssignment { ClientId = clientId, Role = role });
        Debug.Log($"[PlayerRoleManager] Assigned client {clientId} -> {role}");
    }

    /// <summary>Role assigned to this local peer. Defaults to Operations until assigned/synced.</summary>
    public PlayerRole GetLocalPlayerRole()
    {
        if (NetworkManager.Singleton == null) return PlayerRole.Operations;
        ulong me = NetworkManager.Singleton.LocalClientId;
        foreach (var a in _assignments)
            if (a.ClientId == me) return a.Role;
        return PlayerRole.Operations;
    }

    /// <summary>Maps a role to the report SyncDataType that backs its per-player report panel.</summary>
    public static SyncDataType RoleToReport(PlayerRole role)
    {
        return role switch
        {
            PlayerRole.Operations => SyncDataType.OperationsReport,
            PlayerRole.Maintenance => SyncDataType.MaintenanceReport,
            PlayerRole.HR => SyncDataType.HrReport,
            PlayerRole.Finance => SyncDataType.FinanceReport,
            PlayerRole.Vendor => SyncDataType.VendorReport,
            _ => SyncDataType.OperationsReport
        };
    }
}

public struct RoleAssignment : INetworkSerializable, IEquatable<RoleAssignment>
{
    public ulong ClientId;
    public PlayerRole Role;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Role);
    }

    public bool Equals(RoleAssignment other) => ClientId == other.ClientId && Role == other.Role;
}
