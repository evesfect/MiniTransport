# 03 — Roads, Routes & Vehicles

This is the physical transport layer: the road graph buses drive on, how routes are defined, the bus
state machine (driving, stopping at stops, boarding/transfers/fares, breaking down), depots that spawn
and retire buses, and the recovery (tow) vehicle that rescues breakdowns.

```
RoadNode ◀─edges─▶ RoadSegment (spline)        TransportManager
   ▲                    ▲                          • stop registry (stopID → BusStop)
   │ A* (RoadPathfinder)│                          • routes (List<Route>)
   │                    │                          • path cache (stop→stop → List<RoadNode>)
BusStop (on a segment, splineT)                     • RouteNetworkGraph (reachability for transfers)
   ▲
   │ schedule
DepotController ──spawns──▶ BusDriver : VehicleDriver ──serves──▶ PassengerManager
   └────────dispatches────▶ RecoveryVehicle : VehicleDriver
```

---

## The road graph

A road network is a graph of **nodes** and **segments** authored as Unity GameObjects (hand-placed or
imported from OSM — see [07](07-visual-editor-tools.md)).

- [`RoadNode`](../Assets/Scripts/RoadSystem/RoadNode.cs) — a junction. Holds `ConnectedRoads`
  (the segments meeting here) and an optional `OSM_NodeID` for debugging. That's the whole graph
  adjacency.
- [`RoadSegment`](../Assets/Scripts/RoadSystem/RoadSegment.cs) — an edge. Requires a `SplineContainer`
  (Unity Splines) for its geometry, plus `NodeA`/`NodeB` endpoints, a `laneOffset` (so opposing
  directions don't overlap), `SpeedLimit`, and computed `Length`. Key helpers:
  - `GetPointOnRoad(t, headingToNodeB)` — world position at spline parameter `t`, offset to the correct
    lane based on travel direction.
  - `GetConnectedNode(entryNode)` / `IsHeadingToNodeB(entryNode)` — traversal helpers.
  - `GetCost()` = `Length / SpeedLimit` — the A* edge weight.
- [`RoadNetwork`](../Assets/Scripts/RoadSystem/RoadNetwork.cs) — an **editor-time** authoring/validation
  tool (with a custom inspector). Scans for broken links (nulls, dangling endpoints), auto-repairs them,
  snaps nodes & splines to terrain, culls off-map geometry, and generates/clears road meshes. Not used
  at runtime.
- [`SimpleRoadMesh`](../Assets/Scripts/RoadSystem/SimpleRoadMesh.cs) — generates a flat ribbon mesh
  along a segment's spline (left/right verts at `roadWidth`, UVs stretched by length) for the visible
  road surface.

### Pathfinding

[`RoadPathfinder`](../Assets/Scripts/RoadSystem/RoadPathfinder.cs) is a static **A\*** over the node
graph. Edge cost is `RoadSegment.GetCost()` (travel time); the heuristic is straight-line distance / a
nominal max speed (~28 m/s, kept admissible). Two entry points:

- `FindPath(start, end)` — node to node.
- `FindPathToSegment(start, targetSegment)` — node to *either end* of a target segment (used when
  routing to a bus stop, which lives mid-segment). Returns the node list, or `null` if unreachable.

> The open set is a `List` sorted each iteration — fine for small town maps, but the comments flag a
> priority queue as the upgrade for large imports.

---

## Bus stops & routes

- [`BusStop`](../Assets/Scripts/RoadSystem/BusStop.cs) — a stop placed on a segment at spline parameter
  `splineT`. Has a unique `stopID`, a `parentSegment`, and `SnapToSegment()` to position/orient itself
  on the spline. On `Start` it registers with `GridManager` (which buckets it by tile for demand).
- [`Route`](../Assets/Scripts/RoadSystem/TransportManager.cs) — `{ RouteID, RouteName, StopIDs (ordered),
  RouteColor }`. A route is just an ordered list of stop IDs; buses ping-pong along it (forward to the
  end, then reverse).

### TransportManager

[`Assets/Scripts/RoadSystem/TransportManager.cs`](../Assets/Scripts/RoadSystem/TransportManager.cs) owns
all stops and routes (server-authoritative, the standard JSON-sync pattern; routes persist to
`routes.json`).

- **Stop registry:** `RegisterAllStops()` finds every `BusStop` in the scene and maps `stopID → BusStop`.
  `GetStop(id)` looks one up.
- **Routes CRUD:** `AddRouteClient/UpdateRouteClient/DeleteRouteClient` → `RequestRouteOperationRpc` →
  broadcast full list via `SyncRoutesRpc`. Raises `OnRoutesChanged` (consumed by visualizers and the
  reachability graph).
- **Path cache:** `CacheRoute`/`GetPath(startStop, endStop)` precompute and memoize the `RoadNode` path
  between consecutive stops. Crucially it tracks **entry direction** so a bus keeps driving the way it
  entered a stop (no illegal U-turns at a stop): it remembers which node it left the previous stop toward
  and feeds that as the A* start.
- **KPI helpers** (read by `KPIManager`): `StopCoveragePercent()`, `StopsNotCovered()`,
  `LongestRouteStopCount()`.

### RouteNetworkGraph (transfer planning)

[`Assets/Scripts/Systems/RouteNetworkGraph.cs`](../Assets/Scripts/Systems/RouteNetworkGraph.cs) is a
server-side reachability model used to decide whether a passenger should board a given bus, possibly
transferring. It is rebuilt (lazily, on `OnRoutesChanged`) into two maps: `routeID → tiles served` and
`tile → routes serving it`.

- `MinRides(origin, dest)` — BFS over routes; the minimum number of buses ("rides") to get from one tile
  to another, capped by `maxTransfers` (default 2, so ≤3 rides). `transfers = rides − 1`. Memoized.
- `TryPlanLeg(currentTile, upcomingTilesInOrder, dest, out alightTile)` — the **strict-progress board
  rule**: a passenger boards only if some upcoming stop on this bus strictly reduces their remaining
  rides to the destination; `alightTile` is the earliest stop achieving the best progress. Because every
  accepted ride decreases a non-negative integer, journeys always terminate (no infinite hopping).

---

## VehicleDriver (shared movement base)

[`Assets/Scripts/VehicleDriver.cs`](../Assets/Scripts/VehicleDriver.cs) — abstract `NetworkBehaviour`
base for everything that drives along splines (buses and the tow truck). It encapsulates **spline
following with separate server and client path state**:

- A path is a `List<PathLeg>` where each `PathLeg` references a `RoadSegment` plus `StartT/EndT` and a
  `HeadingToB` flag (allowing partial-segment travel and direction).
- The server tracks the authoritative `m_ServerDistanceTraveled`; clients run their own
  `m_ClientDistanceTraveled` with a small `clientSpeedBuffer` (1.1×) so they reach stops slightly ahead
  of the server and never visibly overshoot.
- `UpdateTransformOnSpline` / `CalculatePoint` convert a distance-along-path into a world position +
  tangent (flattened to yaw) and slerp the rotation. `GetCurrentSegmentAndT` reports the live segment &
  spline-t (used by the recovery vehicle to find a broken bus).

Movement is scaled by `SimulationTimeManager.TimeMultiplier`, so pausing/fast-forwarding the clock
pauses/speeds vehicles too.

---

## BusDriver

[`Assets/Scripts/RoadSystem/BusDriver.cs`](../Assets/Scripts/RoadSystem/BusDriver.cs) — the largest
gameplay class. A server-authoritative state machine; clients only render from the replicated
[`BusNetworkState`](../Assets/Scripts/Data/BusDataTypes.cs) (`NetworkVariable`, server-write).

`BusNetworkState` carries current route, previous/target stop, departure time, direction, in-service &
broken-down flags, breakdown stop distance, live passenger count, and breakdown reason — everything a
client needs to animate the bus and show its status without server round-trips.

**Lifecycle:**

1. `DepotController.SpawnBus` instantiates the prefab at the first stop, `NetworkObject.Spawn()`s it, and
   calls `ServerInitialize(busData, depot)` which resolves the route and sets the bus waiting at stop 0.
2. **Server update loop:** while in service, the bus alternates *waiting at a stop* (a timer that scales
   with how many passengers board/alight, `timePerPassenger`) and *driving a leg* (advancing distance
   along the cached path to the next stop). At each stop it runs `ServerHandlePassengers`.
3. **Direction:** route index advances forward to the last stop then reverses (`IsReverseDirection`),
   ping-ponging. (A loop route where first stop == last stop wraps instead.)

**`ServerHandlePassengers(stop)` — the economic heart at every stop:**

- *Drop-off:* onboard groups whose `AlightTile` == this tile leave the bus. If it's their **final**
  destination, they count as served (satisfaction reward scaled by the bus's **Interior** part health,
  0.7×–1.2×) and pay the full fare. If it's a **transfer** point, they're re-queued at this stop toward
  their final destination via `PassengerManager.AddPassengers`, a transfer is recorded on
  `CompanyManager.RecordTransfer`, and they pay a **discounted** fare.
- *Fares:* `CompanyManager.TicketPrice` per passenger per leg, transfer legs multiplied by
  `(1 − TransferDiscount)` (both GM-tunable). All fares at a stop are banked as one `TicketRevenue`
  income line.
- *Pick-up:* if the shift is active and there's capacity, for each waiting group it asks
  `RouteNetworkGraph.TryPlanLeg` (using the bus's upcoming tiles in order) whether boarding makes
  progress; if so it loads them with their planned `alightTile` and removes them from the stop. A
  snapshot of waiting groups is taken *before* drop-off so transfer passengers aren't instantly
  re-boarded onto the same bus.

**Breakdowns:** when `MaintenanceManager` decides a critical part has failed it calls
`SetBrokenDown(true, reason)`; the bus coasts to a `BreakdownStopDistance` and halts, notifying
`MaintenanceManager.OnBusStopped` (which dispatches recovery). `IsFullyStopped`/`IsBroken`/`PassengerCount`
are exposed for the depot and maintenance logic.

---

## DepotController

[`Assets/Scripts/Managers/DepotController.cs`](../Assets/Scripts/Managers/DepotController.cs) — a
per-depot `NetworkBehaviour` (note: **not** a global singleton — there can be several). Each depot has a
`depotID`, a bus prefab, a recovery-vehicle prefab, and a `SpawnNode` for the tow truck.

- **Scheduling:** subscribes to `OnMinuteChanged` → `CheckSchedules()`. For each bus assigned to this
  depot it compares the current time to the bus's `Schedule.StartTime/EndTime`:
  - In-window + healthy enough (`IsBusConditionGoodEnough`, vs. the maintenance operational threshold) +
    parked → **spawn** it onto its route (`SpawnBus`, registers with `FleetManager`).
  - Out-of-window + active + not broken + empty of passengers → **return** it (`ReturnBusToDepot`,
    despawns and unregisters). A bus flagged `PendingSale` is sold (income to `CompanyManager`) on
    return instead of re-parking.
- **Recovery dispatch:** `DispatchRecoveryVehicle(busID)` spawns/reuses one `RecoveryVehicle` and sends
  it on a mission; `IsRecoveryAvailable` gates concurrency; `OnRecoveryVehicleFinished` tells
  `MaintenanceManager` the depot is free again.
- `disableMaintenanceChecks` is a debug flag to let any bus spawn regardless of wear.

---

## RecoveryVehicle

[`Assets/Scripts/Vehicles/RecoveryVehicle.cs`](../Assets/Scripts/Vehicles/RecoveryVehicle.cs) — the tow
truck, another `VehicleDriver`. Its replicated [`RecoveryNetworkState`] carries a state-machine enum
(`Idle → MovingToTarget → Repairing → Returning → Refilling`), the owner depot, the target bus's
segment/spline-t/entry direction, and a departure time. The server drives it to a broken-down bus
(located via the bus's `GetCurrentSegmentAndT`), repairs it in the field at `repairRatePerSecond`,
returns to the depot, and reports completion so the next queued job can dispatch. `MarkerSpawner` debug
markers can visualize its target.

---

## Adding a new vehicle type

1. Subclass `VehicleDriver`, define a small `INetworkSerializable` + `IEquatable` state struct, expose it
   as a server-write `NetworkVariable`.
2. Build your path as `List<PathLeg>` (reuse `RoadPathfinder` + `AddPathLeg`).
3. Drive `m_ServerDistanceTraveled` on the server; render from the replicated state on clients.
4. Spawn it from a server-side manager/depot with `NetworkObject.Spawn()`.
