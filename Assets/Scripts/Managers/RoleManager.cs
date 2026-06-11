using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-60)] // Runs early
public class RoleManager : NetworkBehaviour
{
    public static RoleManager Instance { get; private set; }

    // Maps ClientID to their chosen role
    private Dictionary<ulong, PlayerRole> _playerRoles = new Dictionary<ulong, PlayerRole>();
    
    public event Action OnRolesUpdated;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Call this from your pre-game UI
    public void SelectRole(PlayerRole requestedRole)
    {
        if (IsServer) ClaimRoleInternal(NetworkManager.Singleton.LocalClientId, requestedRole);
        else RequestRoleServerRpc(requestedRole);
    }

    public PlayerRole GetMyRole()
    {
        ulong myId = NetworkManager.Singleton.LocalClientId;
        return _playerRoles.ContainsKey(myId) ? _playerRoles[myId] : PlayerRole.None;
    }

    public bool IsRoleTaken(PlayerRole role)
    {
        return _playerRoles.ContainsValue(role);
    }

    // Maps a player role to the end-of-game report (SyncDataType) it should see.
    // Vendor KPIs have no dedicated role; the Finance manager surfaces them separately.
    public static SyncDataType RoleToReport(PlayerRole role) => role switch
    {
        PlayerRole.GeneralManager     => SyncDataType.GeneralReport,
        PlayerRole.TransportManager   => SyncDataType.OperationsReport,
        PlayerRole.MaintenanceManager => SyncDataType.MaintenanceReport,
        PlayerRole.HRManager          => SyncDataType.HrReport,
        PlayerRole.FinanceManager     => SyncDataType.FinanceReport,
        _                             => SyncDataType.GeneralReport
    };

    private void ClaimRoleInternal(ulong clientId, PlayerRole requestedRole)
    {
        if (requestedRole != PlayerRole.None && IsRoleTaken(requestedRole))
        {
            Debug.LogWarning($"[RoleManager] Role {requestedRole} is already taken!");
            return; // Rejected by server
        }

        _playerRoles[clientId] = requestedRole;
        SyncRolesClientRpc(SerializeRoles());
    }

    [Rpc(SendTo.Server)]
    private void RequestRoleServerRpc(PlayerRole requestedRole, RpcParams rpcParams = default)
    {
        ClaimRoleInternal(rpcParams.Receive.SenderClientId, requestedRole);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncRolesClientRpc(string jsonMap)
    {
        var wrapper = JsonUtility.FromJson<RoleMapWrapper>(jsonMap);
        _playerRoles.Clear();
        for (int i = 0; i < wrapper.ClientIds.Count; i++)
        {
            _playerRoles[wrapper.ClientIds[i]] = wrapper.Roles[i];
        }
        OnRolesUpdated?.Invoke();
    }

    private string SerializeRoles()
    {
        var wrapper = new RoleMapWrapper
        {
            ClientIds = _playerRoles.Keys.ToList(),
            Roles = _playerRoles.Values.ToList()
        };
        return JsonUtility.ToJson(wrapper);
    }

    [Serializable]
    private class RoleMapWrapper
    {
        public List<ulong> ClientIds;
        public List<PlayerRole> Roles;
    }
}