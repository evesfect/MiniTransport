# 08 — Conventions, Gotchas & Extension Checklist

Practical guidance for working in this codebase without breaking the networking model.

---

## Coding conventions

- **One manager per subsystem**, a `NetworkBehaviour` singleton (`public static Instance`) guarded in
  `Awake`, usually `DontDestroyOnLoad`. Set `[DefaultExecutionOrder]` relative to dependencies
  ([01 §2](01-architecture.md#2-manager-singletons)).
- **Server authority.** Public methods do `if (IsServer) DoInternal() else RequestRpc()`. Mutations
  happen only in server-side `*Internal` methods, which then save + broadcast.
- **`[Header]` / `[Tooltip]`** on inspector fields; `[HideInInspector]` for public runtime-only fields.
- **Events over polling.** Subscribe to `SimulationTimeManager` time events and manager `OnXChanged`
  events; don't poll in `Update` unless you genuinely need per-frame work. Always unsubscribe in
  `OnNetworkDespawn`/`OnDisable`.
- **JSON for collections, `NetworkVariable` for hot scalars** ([01 §1](01-architecture.md#1-server-authority)).
- **Layer masks + raycasts** for spatial/terrain queries (consistent with the camera/grid code).
- Naming: managers end in `Manager`, road graph types in `Road*`, UI cards in `*CardDisplay`/`*Card`,
  scroll managers in `*ScrollManager`, networked state structs in `*NetworkState`.

---

## Gotchas

- **Time, not real seconds.** Almost all gameplay is driven by `SimulationTimeManager` events and the
  grid sim tick. If you write logic in `Update`, remember to scale by `TimeMultiplier` (and that the clock
  can be paused/locked).
- **`VisualTime` vs `CurrentTimeOfDay`.** Server logic uses `CurrentTimeOfDay`; anything that must look
  synchronized across peers (grid visuals) uses `VisualTime` (server-truth on host, smoothed on clients).
  Don't mix them.
- **Grid resolution must match on all peers.** A mismatch makes `SendFullStateClientRpc` reject the state.
- **`DepotController` is not a singleton.** There can be many depots; find them with
  `FindObjectsByType<DepotController>` (as `MaintenanceManager` does) or by `depotID`.
- **Host double-apply.** RPCs sent `ClientsAndHost` loop back on the host but not on a dedicated server;
  several systems (e.g. `GridManager.ScheduleTileUpdate`) special-case `!IsHost` to keep dedicated-server
  state in sync. Mind this when sending self-targeted RPCs.
- **Sync handlers must early-out on the server.** Client-apply RPCs (`Sync*Rpc`) start with
  `if (IsServer) return;` (or fire only the UI event on host) so the authoritative copy isn't overwritten
  by its own broadcast.
- **Saves can be stale.** Deleting one JSON file but not the others can desync subsystems that reference
  shared IDs (e.g. `routes.json` referencing stop IDs, `fleet.json` referencing depot IDs). Reset related
  files together.
- **`CompanyManager` save is deferred** (5s buffer, swallows `IOException`). Don't assume the file is
  on disk immediately after a transaction.
- **The root `CLAUDE.md` is outdated** — it documents only the original RTS camera and claims `Scripts/`
  is empty. Trust the code and these docs.

---

## Checklist: adding a new server-authoritative system

1. **Data type.** Add a `[Serializable]` class/struct (JSON) or `INetworkSerializable` struct (netcode),
   in `Data/`. Implement `IEquatable<T>` if it backs a `NetworkVariable`.
2. **Manager.** New `NetworkBehaviour` singleton with `[DefaultExecutionOrder]` after its dependencies.
   - `Awake`: singleton guard.
   - `OnNetworkSpawn` (server): load from disk, subscribe to time/company events you need.
   - `OnNetworkDespawn`: unsubscribe everything.
3. **Public API + RPC split.** `Public → if(IsServer) Internal else RequestRpc → Internal`. Internal
   mutates, saves, and broadcasts (`SyncXRpc`, with `if(IsServer) return;` guard on apply).
4. **Late joiners.** Subscribe to `NetworkManager.OnClientConnectedCallback` and send the new client the
   current state via `RpcTarget.Single`.
5. **Persistence.** `Serialize/Save/Load` with `JsonUtility`; editor → `Assets/x.json`, build →
   `persistentDataPath`.
6. **Live dashboard data?** Wire it into the sync layer ([05 §"Wiring a new dashboard value"](05-data-sync-and-kpi.md#wiring-a-new-dashboard-value)).
7. **End-of-game metric?** Raise an event the manager already has, subscribe `KPIManager` to it, add a
   field to the relevant report struct/builder.
8. **UI.** New `BasePanel` + controller reading from `ClientDataCache`/manager events; gate its button in
   `RequestButtonAccess` if role-specific.
9. **Role gating.** If it's owned by one role, add it to `RequestButtonAccess.CheckAccess`.

---

## Quick reference: who owns what

| Concern | Owner | Persisted file |
|--------|-------|----------------|
| Clock, days, speed | `SimulationTimeManager` | — (network) |
| Money, ledger, satisfaction, fares | `CompanyManager` | `company.json` |
| Buses (data + live instances) | `FleetManager` | `fleet.json` |
| Part decay, breakdowns, repairs, work queue | `MaintenanceManager` | — |
| Mechanics, hiring, training, teams, fatigue | `EmployeeManager` | `employees.json` |
| Vendors, deals, orders, deliveries | `VendorManager` | `vendors.json` |
| Spare parts stock | `InventoryManager` | `inventory.json` |
| Cross-role requests/approvals | `RequestManager` | `requests.json` |
| Stops, routes, paths | `TransportManager` | `routes.json` |
| Transfer reachability | `RouteNetworkGraph` | — |
| Tile world (pop/jobs/traffic/demand) | `GridManager` | — (presets) |
| World evolution (growth/weather/events) | `SimulationDirector` | — |
| Passenger spawning | `DemandSimulationSystem` | — |
| Waiting passengers / patience | `PassengerManager` | — |
| Bus spawning/retiring, recovery dispatch | `DepotController` (per depot) | — |
| Roles | `RoleManager` | — |
| End-of-game reports | `KPIManager` | — |
| Game-over conditions | `GameEndManager` | — |
| Data sync throttling | `NetworkSyncBroker` / `LocalDataBroker` / `ClientDataCache` | — |
