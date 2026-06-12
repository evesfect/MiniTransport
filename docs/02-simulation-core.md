# 02 — Simulation Core (Time, Grid, Demand, Director, Passengers)

This is the "world model": a tile grid that holds population/jobs/traffic/demand, a set of server-side
systems that evolve it each tick, and the passenger economy those systems feed.

```
SimulationTimeManager  ──(game-minutes)──▶  GridSimulationManager
                                                  │ every simulationStepMinutes (15)
                                                  ▼  OnSimulationTick(minutesPassed)
                           ┌───────────────────────────────────────────┐
                           │  GridSimulationSystem subclasses (server)  │
                           │   • SimulationDirector  (growth/weather/   │
                           │      events → modifies grid)               │
                           │   • DemandSimulationSystem (spawns pax)    │
                           └───────────────────────────────────────────┘
                                                  │ reads/writes
                                                  ▼
                                            GridManager (TileData[])
                                                  │ spawns passengers into
                                                  ▼
                                            PassengerManager (per-stop queues)
```

---

## GridManager

[`Assets/Scripts/Managers/GridManager.cs`](../Assets/Scripts/Managers/GridManager.cs) — the spatial
backbone. A flat array of `TileData` laid over the terrain.

- **Dimensions:** `resolutionX × resolutionZ` tiles. Cell size is derived from the active `Terrain`
  bounds (or a 5×5 fallback). Index math: `GetIndex(x,y) = y*resolutionX + x`.
- **Coordinate helpers:** `WorldToGrid(worldPos, out x, out y)`, `GridToWorld(x,y)` (samples terrain
  height), `GetXY(index, …)`.
- **Tile payload** ([`TileData`](../Assets/Scripts/Data/GridDataTypes.cs), an `INetworkSerializable`
  struct): `Traffic` (0–100), `Population`, `Jobs`, `InDemand`/`OutDemand` (0–255 bytes),
  residential/commercial/industrial ratios (sum to 100), and `EcoClass` (Low/Medium/High).
- **Stop registry:** `RegisterStop(BusStop)` buckets stops by tile so demand can ask "are there stops
  in this tile?" via `GetStopsInTile(index)`. Bus stops self-register on `Start`.
- **Traffic → speed:** `GetTrafficModifierAt(worldPos)` maps tile traffic to a 1.0→0.2 speed multiplier
  used by vehicles.
- **Scheduled updates / networking:** see [01-architecture.md §5](01-architecture.md#5-the-grid-update-scheduling-trick).
  `ScheduleTileUpdate` (server) → time-stamped `TileUpdatePacket` → buffered and applied on every peer
  at the same `VisualTime`. Late joiners pull the full grid with `RequestGridStateServerRpc`.
- **Presets:** can bake initial tile values from texture-based [`GridMapPreset`](../Assets/Scripts/Data/GridMapPreset.cs)
  assets via `LoadPreset` (server) → `GridInitializer.ApplyPreset`. See
  [07-visual-editor-tools.md](07-visual-editor-tools.md#gridinitializer--grid-presets).
- Editor gizmos draw the grid and (in play mode) per-tile Pop/Traffic/Jobs/Demand labels.

---

## The simulation tick loop

[`GridSimulationManager`](../Assets/Scripts/Managers/GridSimulationManager.cs) holds a list of
[`GridSimulationSystem`](../Assets/Scripts/Systems/GridSimulationSystem.cs) components (auto-collected
from the same GameObject if the list is empty). On the server it tracks elapsed game-minutes and, once
`simulationStepMinutes` (default **15 game-minutes**) have passed, calls `OnSimulationTick(minutesPassed)`
on each enabled system. `GridSimulationSystem` is an abstract `NetworkBehaviour` with an `Initialize(grid)`
hook and the abstract `OnSimulationTick`.

Two systems ship today, run **in list order** (Director should run before Demand so its modifiers apply
to the same tick):

---

## SimulationDirector

[`Assets/Scripts/Systems/SimulationDirector.cs`](../Assets/Scripts/Systems/SimulationDirector.cs) — the
"game master" that makes the world non-static. Also a singleton (`SimulationDirector.Instance`) so the
demand system can query it. Four features:

1. **Global growth / difficulty ramp.** Adds population & jobs to *every* tile per game-hour, and
   slowly raises a global `CurrentSpawnRateMultiplier` (so late-game demand exceeds the byte cap).
   Fractional growth is accumulated per tile so small per-tick amounts eventually tick a whole unit.
2. **Player impact.** Tiles that contain at least one bus stop grow faster (extra pop/jobs per stop) —
   serving an area makes it denser, increasing future ridership.
3. **Weather** (`Clear/Rain/Snow/Storm`). Random durations; weather adds a traffic penalty that fades
   when it clears, and multiplies demand down (rain 0.8×, snow 0.5×, storm 0.2×) and spawn rates.
4. **Special events** ("Match Day"). On a cooldown, picks a random tile and runs an 8-hour timeline
   (−3h prep → 0–3h event → aftermath) driven by `AnimationCurve`s: a huge **in**-demand spike to the
   event tile before/at start, a huge **out**-demand spike when it ends, and traffic jams around both.

It exposes `ApplyDemandModifiers(tile, ref out, ref in)` and `GetDirectSpawnMultiplier(tile)` which the
demand system calls per tile per tick. It writes traffic back to the grid via `ScheduleTileUpdate`.

---

## DemandSimulationSystem

[`Assets/Scripts/Systems/DemandSimulationSystem.cs`](../Assets/Scripts/Systems/DemandSimulationSystem.cs)
— turns tile economics into actual waiting passengers. Each tick (server-only):

1. **Time-of-day curves.** Six `AnimationCurve`s (residential/commercial/industrial × out/in) over a
   24h axis describe when people leave home vs. go to work/shops. Evaluated once per tick.
2. **Per-tile demand.** For each tile, `OutDemand` ≈ `Population×resRatio×timeOut + Jobs×(com+ind)Ratio×timeOut`,
   adjusted by economic class, then run through `SimulationDirector.ApplyDemandModifiers`, clamped into
   the 0–100 byte. `InDemand` is computed symmetrically and used as the **destination attractiveness
   weight**. Changed demand values are scheduled to the grid (low-priority `DemandValues` mask).
3. **Spawning.** For tiles that have stops and nonzero out-demand, `spawnChance = OutDemand × globalSpawnRate
   × (minutes/60) × directorMultiplier`; the fractional part becomes a probabilistic extra spawn. Each
   spawned passenger is assigned a **weighted-random destination tile** (roulette wheel over in-demand),
   skipping the source tile and tiles with no stops, then handed to `PassengerManager.AddPassengers`.

This is the producer side of the passenger economy; `BusDriver` (see
[03-road-and-vehicles.md](03-road-and-vehicles.md)) is the consumer.

---

## PassengerManager

[`Assets/Scripts/Managers/PassengerManager.cs`](../Assets/Scripts/Managers/PassengerManager.cs) — the
authoritative store of who is waiting where, and their patience.

- **State:** `Dictionary<stopID, List<WaitingPassengerGroup>>`. A
  [`WaitingPassengerGroup`](../Assets/Scripts/Data/PassengerDataTypes.cs) is `{ DestinationTileIndex,
  PassengerCount, SpawnTime }` — passengers heading to the same destination from the same stop are
  **merged into one group** to keep counts compact.
- **API (server):** `AddPassengers(stopID, destTile, count)` (merges or creates a group and resyncs the
  stop), `RemovePassengers(stopID, destTile, count)` (called when a bus boards them).
- **Patience:** every ~600 frames `CheckPassengerTimeouts` removes groups that have waited longer than
  `maxWaitTimeHours` (default 2.5h, with day-wrap handling). Timed-out passengers apply a satisfaction
  penalty to `CompanyManager` and raise `OnPassengersTimedOut` (a KPI signal).
- **KPI hooks:** `OnPassengersServed(waitHours, count)` fires from `RemovePassengers` when a group
  boards (used for on-time / avg-wait metrics); `OnPassengersTimedOut(count)` for missed demand.
- **Sync:** `SyncStopPassengersClientRpc` ships the changed stop's group array to clients so the HUD's
  per-stop waiting list is accurate. Clients read via `GetPassengersAtStop`.

> **Transfers** are not stored here as a journey identity — when a passenger alights mid-journey to
> transfer, `BusDriver` re-adds them to the destination stop with `AddPassengers` and records a transfer
> on `CompanyManager`. See the boarding logic in [03-road-and-vehicles.md](03-road-and-vehicles.md).

---

## GridEvents

[`Assets/Scripts/Systems/GridEvents.cs`](../Assets/Scripts/Systems/GridEvents.cs) — a tiny static event
bus (`OnPopulationChanged`) for decoupled grid notifications. Lightweight extension point; not heavily
used yet.

---

## Tuning cheat-sheet

| Want to change… | Look at |
|---|---|
| World clock speed / day length | `SimulationTimeManager.baseMinutesPerSecond`, `GameEndManager.gameLengthDays` |
| How often demand recalculates | `GridSimulationManager.simulationStepMinutes` |
| Rider volume | `DemandSimulationSystem.globalSpawnRate` + the 6 time curves |
| Difficulty ramp | `SimulationDirector.globalPop/JobGrowthPerHour`, `spawnRateGrowthPerHour` |
| Weather/event severity | `SimulationDirector` weather penalties + event curves |
| Passenger patience | `PassengerManager.maxWaitTimeHours` |
| Grid resolution | `GridManager.resolutionX/Z` (must match across peers) |
