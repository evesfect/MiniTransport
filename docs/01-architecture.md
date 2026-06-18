# 01 — Architecture

This document explains the structural patterns every other module follows. Read it first; the
per-subsystem docs assume you know these conventions.

---

## 1. Server authority

MiniTransport is **strictly server-authoritative**. The server (which may be a dedicated server or
the Host) owns all gameplay state. Clients are thin: they render, collect input, and send requests.

The recurring pattern in every manager:

```csharp
public void DoThing(args)
{
    if (IsServer) DoThingInternal(args);   // run it directly
    else RequestDoThingRpc(args);          // ask the server to run it
}

[Rpc(SendTo.Server)]
private void RequestDoThingRpc(args) => DoThingInternal(args);

private void DoThingInternal(args)         // server-only mutation
{
    // ...mutate authoritative state...
    SaveX();                               // persist to disk
    SyncXRpc(SerializeX());                // broadcast new state to all clients
}
```

- Public methods are **callable from any peer** (the UI calls them without caring whether it's the host).
- The `if (IsServer) … else …RequestRpc` split routes the call to the authority.
- `[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]` is used when *any* client
  (not just the object owner) must be able to invoke — typical for shared singletons.
- State is broadcast back with `[Rpc(SendTo.ClientsAndHost)]` carrying a JSON snapshot, or via
  `NetworkVariable<T>` for small frequently-read values.

### Why JSON-over-RPC instead of `NetworkVariable` everywhere?

Most managers hold **collections** (lists of buses, employees, vendors, routes, requests). Netcode's
`NetworkList`/`NetworkVariable` are awkward for variable-length nested data, so the project serializes
the whole collection with `JsonUtility` and ships it as a string. This is simple and robust; the cost
is full-snapshot syncs (acceptable here because these collections are small and change infrequently).
For **scalar, high-read** values (balance mirror, satisfaction, time of day, active bus count) the
project does use `NetworkVariable<T>` so clients can read them every frame without an RPC.

---

## 2. Manager singletons

Each subsystem is a `NetworkBehaviour` exposing a `public static Instance`. They live on persistent
GameObjects (most call `DontDestroyOnLoad`). The singleton guard is uniform:

```csharp
private void Awake()
{
    if (Instance != null && Instance != this) { Destroy(gameObject); return; }
    Instance = this;
    DontDestroyOnLoad(gameObject);
}
```

### Execution order matters

Managers declare `[DefaultExecutionOrder(...)]` so they initialize in dependency order. Lower numbers
run earlier:

| Order | Manager | Role |
|-------|---------|------|
| -60 | `SimulationTimeManager`, `RoleManager` | The clock and role registry — everything depends on these |
| -55 | `CompanyManager` | Money/ledger; many systems bill it |
| -50 | `FleetManager`, `GridManager`, `EmployeeManager`, `TransportManager` | Core domain data |
| -49 | `VendorManager` | |
| -45 | `MaintenanceManager`, `RequestManager`, `PassengerManager` | Depend on fleet/time/grid |
| -40 | `GridSimulationManager`, `KPIManager` | Run after the data they read |
| -30 | `GameEndManager` | Reads company/time/KPI |
| -10 | `NetworkSyncBroker` | Sync throttler |

When you add a manager that reads another at spawn time, give it a **higher** order number than its
dependencies.

---

## 3. The simulation clock

[`SimulationTimeManager`](../Assets/Scripts/Managers/SimulationTimeManager.cs) is the metronome the
whole game runs on. There is **no real-time gameplay** — everything is driven by in-game time.

- The server advances a `NetworkVariable<float> _netTimeOfDay` (0–24h) and `_netDay`. Speed is
  `baseMinutesPerSecond * timeMultiplier` (multiplier 0–100, settable by any client via
  `RequestTimeMultiplierRpc` — this is the Pause/1×/3×/10× HUD control).
- It raises three C# events that the rest of the game subscribes to instead of polling:
  - `OnMinuteChanged` — fine-grained ticks (bus part decay, depot schedule checks)
  - `OnHourChanged` — hourly batch work (maintenance crews, fatigue, vendor deliveries, KPI sampling)
  - `OnDayChanged` — daily/weekly logic (payroll & bills on day % 7, training, game-end checks)
- **Client time smoothing:** clients don't get a time update every frame, so they locally extrapolate
  `_clientVisualTime` from the last synced server time × multiplier. `VisualTime` returns the server
  value on the server and the smoothed value on clients. The grid update scheduler uses `VisualTime`
  so visual state flips at the same wall-clock moment on every peer (see §5).
- `LockTime()` freezes the clock and blocks further speed changes — used by `GameEndManager`.

**Weekly billing** is derived: `CompanyManager` listens to `OnDayChanged` and fires
`OnWeeklyExpensesRequested` when `day % 7 == 0`. Fleet tax, payroll, staff upkeep, and the weekly
vendor refresh all hang off that single signal.

---

## 4. Two tick systems

There are two distinct "tick" mechanisms; don't confuse them:

1. **Time events** (`OnMinuteChanged/OnHourChanged/OnDayChanged`) — discrete, server-side, used by
   managers for economy and maintenance logic.
2. **Grid simulation tick** — [`GridSimulationManager`](../Assets/Scripts/Managers/GridSimulationManager.cs)
   accumulates game-minutes and, every `simulationStepMinutes` (default 15 game-minutes), calls
   `OnSimulationTick(minutesPassed)` on every attached [`GridSimulationSystem`](../Assets/Scripts/Systems/GridSimulationSystem.cs).
   The demand model, simulation director, etc. are these systems. They run **server-only** and are
   initialized with a reference to `GridManager`. See [02-simulation-core.md](02-simulation-core.md).

---

## 5. The grid update scheduling trick

The simulation mutates tile data on the server, but visuals (heatmaps, demand circles) must change at
the **same moment** on every peer despite network latency. [`GridManager`](../Assets/Scripts/Managers/GridManager.cs)
solves this with **time-stamped scheduled updates**:

- The server computes new tile data and calls `ScheduleTileUpdate(index, data, mask)`.
- That broadcasts a `TileUpdatePacket` tagged with an **execution game-time** (`now + scheduleLookaheadHours`,
  default +0.5h) via `ScheduleGridUpdateClientRpc`.
- Every peer (including the server) buffers the packet and only applies it when its local `VisualTime`
  reaches the stamped time. Because all peers share the same logical clock, the change appears
  simultaneously everywhere.
- `TileUpdatePacket` carries a `TileUpdateFlags` bitmask so only the changed fields are serialized and
  applied (traffic / population / jobs / demand / ratios / economy).

Late-joining clients request the full grid once via `RequestGridStateServerRpc` → `SendFullStateClientRpc`.

---

## 6. The data-sync interest model

The HUD/dashboards must not poll the network. Instead there is a **subscription/interest** layer
(detailed in [05-data-sync-and-kpi.md](05-data-sync-and-kpi.md)):

- A UI panel that wants live data calls `LocalDataBroker.RegisterInterest(SyncDataType.X)`.
- On a client, that sends a `SubscribeRpc` to the server's [`NetworkSyncBroker`](../Assets/Scripts/Systems/NetworkSyncBroker.cs),
  which tracks subscribers per data type and **rate-limits** outbound syncs (e.g. company stats every
  0.5s, fleet every 2s, reports every 5s) so an idle dashboard doesn't flood the wire.
- Managers call `NetworkSyncBroker.MarkDirty(type)` when their data changes; the broker decides when to
  actually send, and only to subscribed clients.
- Synced values land in a [`ClientDataCache`](../Assets/Scripts/Data/ClientDataCache.cs) ScriptableObject
  that raises C# events; UI binds to those events. On the host, the same cache is filled directly from
  the live managers (no network hop).

This keeps the common case (nobody looking at a panel) free of traffic, and bounds the cost when many
panels are open.

---

## 7. Networked data types

`Data/` holds the serializable contracts. Two flavours:

- **`[Serializable]` classes/structs** shipped as JSON strings (`BusData`, `EmployeeData`, `Route`,
  `GameRequest`, `VendorData`, the report structs in `SyncDataTypes.cs`, …). These can nest freely.
- **`INetworkSerializable` structs** sent as real netcode payloads (`TileData`, `TileUpdatePacket`,
  `BusNetworkState`, `RecoveryNetworkState`, `WaitingPassengerGroup`). These back `NetworkVariable`s
  or direct RPC params and use fixed-size types (`FixedString32Bytes`, `byte`, `ushort`) to stay small.

When a struct backs a `NetworkVariable`, it also implements `IEquatable<T>` so netcode can detect
no-op writes.

---

## 8. Save / load

Persistence is intentionally low-tech: each authoritative manager serializes its collection to a JSON
file with `JsonUtility` and reads it back on `OnNetworkSpawn` (server only). Notes:

- Writes are usually immediate on each mutation, except `CompanyManager`, which **buffers** writes
  behind a 5-second timer and swallows transient `IOException`s (OneDrive/AV locking the file) to avoid
  a per-frame exception storm.
- In the editor saves go to `Assets/*.json` (visible, easy to inspect/reset); in builds they go to
  `persistentDataPath`.
- To reset a subsystem, delete its JSON file while the game is stopped.

---

## 9. Putting it together — a request's life

A concrete end-to-end example (Transport Manager asks to buy buses):

1. Transport UI calls `RequestManager.CreateRequest(BuyBus, FinanceManager, amount, payload)`.
2. Client → server RPC creates a `GameRequest` (state `Active`, target = Finance) and syncs it to all
   peers; everyone's request panels refresh.
3. The Finance player approves; `RequestManager.ApproveForwardRequest` forwards it to the GM
   (`AwaitingGeneralManager`).
4. The GM approves; `ExecuteGMApproval` charges `CompanyManager` and adds buses via
   `FleetManager.RequestFleetOperationRpc`.
5. `FleetManager` marks `FleetStats`/`MaintenanceStats` dirty on the sync broker; subscribed dashboards
   update. `KPIManager` (subscribed to fleet/company events) recomputes reports.
6. At the buses' scheduled service hours, the owning `DepotController` spawns them and they start
   driving and collecting fares.

Every arrow above is one of the patterns in this document.
