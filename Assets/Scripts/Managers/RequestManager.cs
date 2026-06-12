using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using System.IO;

[DefaultExecutionOrder(-45)]
public class RequestManager : NetworkBehaviour
{
    public static RequestManager Instance { get; private set; }

    public List<GameRequest> ActiveRequests = new List<GameRequest>();
    public event Action OnRequestsUpdated;

#if UNITY_EDITOR
    private string SavePath => Path.Combine(Application.dataPath, "requests.json");
#else
    private string SavePath => Path.Combine(Application.persistentDataPath, "requests.json");
#endif

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) 
        {
            LoadRequests(); // <-- Load existing requests from disk when server starts
            NetworkManager.Singleton.OnClientConnectedCallback += SyncToNewClient;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= SyncToNewClient;
    }

    private void SyncToNewClient(ulong clientId)
    {
        if (IsServer) SyncRequestsRpc(SerializeRequests(), RpcTarget.Single(clientId, RpcTargetUse.Temp));
    }

    // --- CREATION API ---

    public void CreateRequest(RequestType type, PlayerRole target, int amount, string payload)
    {
        if (IsServer) CreateRequestInternal(RoleManager.Instance.GetMyRole(), type, target, amount, payload);
        else RequestCreateRpc(RoleManager.Instance.GetMyRole(), type, target, amount, payload);
    }

    private void CreateRequestInternal(PlayerRole requester, RequestType type, PlayerRole target, int amount, string payload)
    {
        var req = new GameRequest
        {
            RequestID = Guid.NewGuid().ToString().Substring(0, 8),
            Type = type,
            Requester = requester,
            CurrentTarget = target,
            TargetAmount = amount,
            CurrentAmount = 0,
            Payload = payload,
            State = RequestState.Active
        };

        ActiveRequests.Add(req);
        SyncRequestsRpc(SerializeRequests());
    }

    // --- PROGRESSION & TRACKING API ---

    // Called automatically by HR/Vendor managers when they take actions
public void NotifyActionTaken(RequestType type, int amountAdded, string specificCondition = "")
    {
        if (!IsServer) return;

        bool changed = false;
        foreach (var req in ActiveRequests.Where(r => r.State == RequestState.Active && r.Type == type))
        {
            // Hiring: specificCondition is the Skill Level
            if (type == RequestType.HireMechanic && float.TryParse(specificCondition, out float skill) && float.TryParse(req.Payload, out float reqSkill))
            {
                if (skill < reqSkill) continue; 
            }
            // Parts: specificCondition is the ItemID
            else if (type == RequestType.BuyParts && req.Payload != specificCondition)
            {
                continue; 
            }
            // NEW FIX: Training: specificCondition is the EmployeeID. Check if it's in the comma-separated Payload!
            else if (type == RequestType.TrainMechanic && !req.Payload.Contains(specificCondition))
            {
                continue;
            }

            req.CurrentAmount += amountAdded;
            if (req.CurrentAmount >= req.TargetAmount)
            {
                req.CurrentAmount = req.TargetAmount;
                req.State = RequestState.Completed;
            }
            changed = true;
        }

        if (changed) SyncRequestsRpc(SerializeRequests());
    }

    // --- APPROVAL & REJECTION API (Two-Tier Flow) ---

    // NEW FIX: Added 'modifiedPayload' parameter
    public void ApproveForwardRequest(string reqId, int approvedAmount, string modifiedPayload = "")
    {
        if (IsServer) ApproveForwardInternal(reqId, approvedAmount, modifiedPayload);
        else ApproveForwardRpc(reqId, approvedAmount, modifiedPayload);
    }

    private void ApproveForwardInternal(string reqId, int approvedAmount, string modifiedPayload = "")
    {
        var req = ActiveRequests.FirstOrDefault(r => r.RequestID == reqId);
        if (req == null) return;

        if (req.CurrentTarget == PlayerRole.FinanceManager)
        {
            req.TargetAmount = approvedAmount; 
            
            // NEW FIX: If Finance only approved specific buses, update the Payload for the GM
            if (!string.IsNullOrEmpty(modifiedPayload)) 
            {
                req.Payload = modifiedPayload; 
            }

            req.CurrentTarget = PlayerRole.GeneralManager;
            req.State = RequestState.AwaitingGeneralManager;
        }
        else if (req.CurrentTarget == PlayerRole.GeneralManager)
        {
            req.State = RequestState.Completed;
            req.CurrentAmount = req.TargetAmount;
            ExecuteGMApproval(req);
        }
        
        SyncRequestsRpc(SerializeRequests());
    }

    private void ExecuteGMApproval(GameRequest req)
    {
        if (req.Type == RequestType.BuyBus)
        {
            float totalCost = req.TargetAmount * 15000f; // Placeholder fixed cost
            if (CompanyManager.Instance.TryExecuteActionableTransaction(totalCost, TransactionCategory.VehiclePurchase, "Purchased requested buses"))
            {
                for (int i = 0; i < req.TargetAmount; i++)
                {
                    BusData newBus = new BusData { BusID = $"B-{UnityEngine.Random.Range(1000, 9999)}", Capacity = 40 };
                    FleetManager.Instance.RequestFleetOperationRpc(JsonUtility.ToJson(newBus), FleetManager.FleetOperation.Add);
                }
            }
        }
        else if (req.Type == RequestType.SellBus)
        {
            // This now safely reads the potentially filtered list from Finance
            string[] busIds = req.Payload.Split(',');
            int soldCount = 0;

            foreach (var id in busIds)
            {
                // Clean up any accidental spaces
                string cleanId = id.Trim();
                if (string.IsNullOrEmpty(cleanId)) continue;
                if (soldCount >= req.TargetAmount) break;

                // Find the bus in the fleet
                var bus = FleetManager.Instance.allBuses.FirstOrDefault(b => b.BusID == cleanId);
                if (bus != null)
                {
                        bool isBusActive = FleetManager.Instance != null && FleetManager.Instance.IsBusActive(bus.BusID);

                        // Only delay sale if the bus is actively out on a route.
                        // Parked buses can be sold immediately, whether they are assigned to a depot or not.
                        if (!isBusActive)
                    {
                        // If it's already parked in a depot, sell it immediately
                        FleetManager.Instance.RequestFleetOperationRpc(JsonUtility.ToJson(new BusData { BusID = cleanId }), FleetManager.FleetOperation.Remove);
                        CompanyManager.Instance.AddIncome(8000f, TransactionCategory.VehiclePurchase, $"Sold bus {cleanId}");
                    }
                    else
                    {
                        // If it is out driving, flag it for later
                        bus.PendingSale = true;
                        
                        // Ensure this flag is saved/synced! 
                        // [NEW UPDATE HERE] Save locally and flag for network sync
                        FleetManager.Instance.SaveFleet();
                        if (NetworkSyncBroker.Instance != null)
                        {
                            NetworkSyncBroker.Instance.MarkDirty(SyncDataType.FleetStats);
                        }
                    }
                }
                soldCount++;
            }
        }
    }

    public void RejectRequest(string reqId, string reason)
    {
        if (IsServer) RejectInternal(reqId, reason);
        else RejectRpc(reqId, reason);
    }

    private void RejectInternal(string reqId, string reason)
    {
        var req = ActiveRequests.FirstOrDefault(r => r.RequestID == reqId);
        if (req != null)
        {
            req.State = RequestState.Rejected;
            req.RejectReason = reason;
            SyncRequestsRpc(SerializeRequests());
        }
    }

    public void MarkAsRead(string reqId)
    {
        if (IsServer) MarkReadInternal(reqId);
        else MarkReadRpc(reqId);
    }

    private void MarkReadInternal(string reqId)
    {
        var req = ActiveRequests.FirstOrDefault(r => r.RequestID == reqId);
        if (req != null)
        {
            req.State = RequestState.Read;
            SyncRequestsRpc(SerializeRequests());
        }
    }

    // --- RPCS & SERIALIZATION ---

    [Rpc(SendTo.Server)] private void RequestCreateRpc(PlayerRole r, RequestType t, PlayerRole target, int a, string p) => CreateRequestInternal(r, t, target, a, p);
    
    // NEW FIX: RPC now accepts modPayload
    [Rpc(SendTo.Server)] private void ApproveForwardRpc(string id, int amount, string modPayload) => ApproveForwardInternal(id, amount, modPayload);
    
    [Rpc(SendTo.Server)] private void RejectRpc(string id, string reason) => RejectInternal(id, reason);
    [Rpc(SendTo.Server)] private void MarkReadRpc(string id) => MarkReadInternal(id);

    [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
    private void SyncRequestsRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer)
        {
            SaveRequests(); // <--- (Saves to disk whenever state changes)
            OnRequestsUpdated?.Invoke();
            return;
        }
        var container = JsonUtility.FromJson<RequestContainer>(json);
        if (container != null)
        {
            ActiveRequests = container.Requests;
            OnRequestsUpdated?.Invoke();
        }
    }

    [ContextMenu("Save Requests")]
    public void SaveRequests() 
    { 
        File.WriteAllText(SavePath, SerializeRequests()); 
    }

    [ContextMenu("Load Requests")]
    public void LoadRequests()
    {
        if (File.Exists(SavePath))
        {
            var container = JsonUtility.FromJson<RequestContainer>(File.ReadAllText(SavePath));
            if (container != null && container.Requests != null)
            {
                ActiveRequests = container.Requests;
                Debug.Log($"[RequestManager] Loaded {ActiveRequests.Count} active requests.");
            }
        }
    }
    
    private string SerializeRequests() => JsonUtility.ToJson(new RequestContainer { Requests = ActiveRequests });
}