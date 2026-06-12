# MiniTransport — Technical Documentation

MiniTransport is a **multiplayer, role-based public-transport tycoon** built in **Unity 6.2** with
the **Universal Render Pipeline** and **Netcode for GameObjects**. Up to five players each take a
management role (General, Transport, Maintenance, Finance, HR) and cooperatively run a bus company
in a small town: drawing routes, buying and repairing buses, hiring mechanics, negotiating with
parts vendors, and keeping the books balanced for a fixed number of in-game days.

This folder is the entry point for developers who want to understand or extend the project. It is
written assuming you can read C# and have basic Unity familiarity, but **no prior knowledge of this
codebase**.

---

## How to read these docs

| Doc | What it covers |
|-----|----------------|
| [01-architecture.md](01-architecture.md) | The big picture: server-authority model, manager singletons, the simulation clock, networking patterns, save system, and how data flows from server to client. **Read this first.** |
| [02-simulation-core.md](02-simulation-core.md) | The world simulation: time, the demand grid, the simulation director (weather/events/growth), passenger spawning and patience. |
| [03-road-and-vehicles.md](03-road-and-vehicles.md) | The road graph, A* pathfinding, bus stops & routes, the bus driver state machine, passenger boarding/transfers/fares, depots, and the recovery (tow) vehicle. |
| [04-company-and-roles.md](04-company-and-roles.md) | The economy and the per-role gameplay systems: company ledger, fleet, maintenance, HR/employees, vendors, inventory, the cross-role request workflow, and role assignment. |
| [05-data-sync-and-kpi.md](05-data-sync-and-kpi.md) | The interest-based data-sync layer (`NetworkSyncBroker` / `LocalDataBroker` / `ClientDataCache`) and the end-of-game KPI report aggregation (`KPIManager`). |
| [06-ui.md](06-ui.md) | The two UI stacks (uGUI panel system + UI-Toolkit HUD), role-gated buttons, dashboards, report screens, and the card/scroll list pattern. |
| [07-visual-editor-tools.md](07-visual-editor-tools.md) | Non-gameplay code: route/demand/traffic visualizers, ambient traffic, and the editor tooling (OSM road importer, bus-stop placer, building placer, grid presets) plus the debug harnesses. |
| [08-conventions.md](08-conventions.md) | Coding conventions, gotchas, and a checklist for adding a new networked system. |

---

## Project at a glance

- **Engine:** Unity 6.2, URP 17.2
- **Networking:** `com.unity.netcode.gameobjects` 2.7 (client-server, host or dedicated server)
- **Input:** new Input System (`InputSystem_Actions.inputactions`)
- **Roads:** Unity **Splines** package; roads can be hand-authored or imported from **OpenStreetMap** (`OsmSharp` via NuGetForUnity)
- **Tweening/UI:** DOTween, TextMesh Pro, XCharts
- **Multiplayer testing:** ParrelSync + Unity Multiplayer Play Mode

### Source layout (`Assets/Scripts/`)

```
Data/        Serializable data types, enums, network structs, the ClientDataCache ScriptableObject
Managers/    Server-authoritative singletons (one per subsystem) — the heart of the game
Systems/     Cross-cutting systems: data-sync brokers, grid simulation base, route reachability graph
RoadSystem/  Road graph (nodes/segments/splines), pathfinder, bus stops, routes, bus driver, TransportManager
Vehicles/    RecoveryVehicle (tow truck)
VehicleDriver.cs   Shared base class for all spline-following vehicles
RTSCamera/   RTS camera, selection box, QuickOutline (third-party)
UI/          All runtime UI (uGUI panels + UI-Toolkit HUD + report screens + card/scroll managers)
Visual/      Route/demand/traffic visualizers, ambient cars, markers, dissolve VFX
Editor/      Editor-only tooling (OSM importer, bus-stop placer, building placer)
Debug/       In-scene debug harnesses and the host/client connection GUI
Tools/       GridInitializer (bakes texture presets into grid data)
Compat/      Polyfills (IsExternalInit for C# record/init support)
```

### Persistence

The server writes JSON save files (one per subsystem) next to the project in the editor
(`Assets/*.json`) and to `Application.persistentDataPath` in builds:
`company.json`, `fleet.json`, `employees.json`, `vendors.json`, `inventory.json`,
`routes.json`, `requests.json`. Clients never read these — they receive state over the network.

---

## Running the project

1. Open the project in Unity 6.2 (or newer 6.x). Let it import packages (some come from git URLs, so
   you need network access on first import).
2. Open a gameplay scene — **`Assets/Scenes/JeffersonHeights.unity`** is the most complete map; other
   scenes (`Kars`, `DevScene`, `TransportationManagerDevScene`, `Playground`) are work-in-progress maps
   or feature sandboxes.
3. Press Play. A small IMGUI panel (top-right, from [`NetworkManagerUI`](../Assets/Scripts/Debug/NetworkManagerUI.cs))
   lets you start as **Host**, **Server**, or **Client** (with an IP field).
4. Once connected, a role-picker ([`RoleSelectionIMGUI`](../Assets/Scripts/UI/RoleSelectionIMGUI.cs))
   appears. Pick a management role; that gates which UI buttons and panels you can use.
5. For local multiplayer testing, use **ParrelSync** (`Tools` menu) to clone the project, or Unity's
   **Multiplayer Play Mode** virtual players.

> **Note:** The repository's top-level `CLAUDE.md` describes an earlier state of the project where
> `Scripts/` was "empty" and only the RTS camera existed. That file is **out of date** — the game
> logic documented here lives under `Assets/Scripts/`. Treat these docs as the current source of truth.
