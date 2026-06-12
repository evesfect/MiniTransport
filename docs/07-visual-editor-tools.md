# 07 — Visual, Editor Tools, Debug & Misc

Non-gameplay-logic code: runtime visualizers and ambient flavour, the editor authoring tools, the debug
harnesses, and the camera/selection systems.

---

## Visualizers (`Visual/`)

These render the (networked) simulation state for the player. They read from the managers/grid; they
never mutate authoritative state.

- [`RouteVisualizer`](../Assets/Scripts/Visual/RouteVis/RouteVisualizer.cs) (singleton) — draws each
  active route as a smoothed, color-coded `LineRenderer` along its stops, with stop markers, corner
  rounding, height-stacking for overlapping routes, and highlight/grey-out of a selected route. Rebuilds
  on `TransportManager.OnRoutesChanged`. Toggled from the HUD's Inspection dropdown.
- [`DemandCircleVisualizer`](../Assets/Scripts/Visual/DemandCircleVisualizer.cs) — per-tile circles sized
  by in/out demand (procedural circle mesh, threshold to hide low-demand tiles), refreshed on an interval.
- [`TrafficHeatmapVisualizer`](../Assets/Scripts/Visual/TrafficHeatmapVisualizer.cs) — per-tile quad
  heatmap colored green→yellow→red by tile `Traffic`, with adjustable transparency and refresh interval.
- [`MarkerSpawner`](../Assets/Scripts/Visual/Marker/MarkerSpawner.cs) — spawns debug world markers (used
  by `BusDriver`/`RecoveryVehicle` to visualize targets/paths).
- [`DissolveAfterTime`](../Assets/Scripts/Visual/Dissolve/DissolveAfterTime.cs) — a shader-driven dissolve
  VFX that removes an object after a delay.

### Ambient traffic

[`AmbientTrafficManager`](../Assets/Scripts/Visual/AmbientVehicles/AmbientTrafficManager.cs) +
[`AmbientVehicle`](../Assets/Scripts/Visual/AmbientVehicles/AmbientVehicle.cs) — purely cosmetic cars that
populate the roads for life. The manager buckets road segments by grid tile, wakes only the tiles near
the camera frustum (with a buffer), and scales car density by tile traffic/demand, capped at
`maxVehicles`. It's a **client-side visual system** (not networked) tuned for performance via sector
wake-up and staggered update intervals. `AmbientVehicle`s follow segment splines and despawn when their
sector sleeps.

---

## Editor tools (`Editor/`)

Editor-only authoring utilities (`EditorWindow`s and scene-GUI helpers). Not in builds.

- [`OSMImporterWindow`](../Assets/Scripts/Editor/OSMImporterWindow.cs) (`Tools ▸ OSM Road Importer`) —
  imports an OpenStreetMap `.osm` file (via `OsmSharp`) into the project's road graph: parses highway
  ways and nodes, detects intersections, projects lat/lon to local meters at a chosen scale, and
  instantiates `RoadNode`/`RoadSegment` prefabs wired into the graph. This is how a real town's road
  network becomes a playable map.
- [`BusStopPlacer`](../Assets/Scripts/Editor/BusStopPlacer.cs) (`Tools ▸ Toggle Bus Stop Placer (P)`) — a
  scene-view tool that snaps a bus-stop prefab onto the nearest road segment at the correct `splineT`,
  using a spatial grid for fast nearest-segment queries and a live preview.
- [`BuildingPlacer`](../Assets/Scripts/Editor/BuildingPlacer.cs) (`MassBuildingPlacer`) and
  [`BuildingBrushTool`](../Assets/Scripts/Editor/BuildingBrushTool.cs) — bulk/brush placement of building
  props to dress the map.

Related authoring assets:
- [`GridMapPreset`](../Assets/Scripts/Data/GridMapPreset.cs) — a ScriptableObject describing how texture
  channels map onto initial tile values (linear mappings for traffic/ratios/eco-class, distribution
  mappings that spread a total population/jobs budget by pixel density).

### GridInitializer — grid presets

[`GridInitializer`](../Assets/Scripts/Tools/GridInitializer.cs) (`Tools/`) — a static baker that applies a
`GridMapPreset` onto a `TileData[]`: it samples each preset layer's texture and, in two passes, computes
density distributions (population/jobs spread proportionally to pixel intensity, summing to the configured
totals) and linear field mappings. Called by `GridManager.LoadPreset` to seed the world from an authored
image.

---

## Debug harnesses (`Debug/`)

In-scene helpers for testing systems in isolation, typically IMGUI panels or context-menu actions:

- [`NetworkManagerUI`](../Assets/Scripts/Debug/NetworkManagerUI.cs) — the **Host / Server / Client**
  connection panel (IMGUI, top-right) with an IP field and connection-failure messages. This is how you
  start a session. Reads/sets `UnityTransport` connection data.
- `CompanyDebugBalanceSimulator`, `FleetDebugger`, `EmployeeDebugger`, `VendorDebugger`,
  `InventoryDebugger`, `GridDebugger`, `RouteDebugger` — per-subsystem test rigs that exercise the
  managers' APIs (add money, spawn buses, hire mechanics, place orders, paint grid values, build routes)
  without the full UI. Handy when developing one system.

---

## RTS Camera & selection (`RTSCamera/`)

The original camera framework (predates the game logic). Still the in-game camera.

- [`RTSCameraController`](../Assets/Scripts/RTSCamera/RTSCameraController.cs) — orbital RTS camera (zoom,
  rotate, pan) with terrain-aware focus and an object-tracking API (`StartTrackingObject`,
  `FocusOnObject`, `StopTracking`) used to follow buses.
- [`SelectionController`](../Assets/Scripts/RTSCamera/SelectionController.cs) (`SelectionBoxController`) —
  3D terrain-following and 2D screen-space selection boxes, multi-select, and F-to-focus, with QuickOutline
  highlight feedback.
- `QuickOutline/Outline.cs` — third-party outline shader system used for selection highlights.

Both use the new Input System ("Camera" action map). See the repository root `CLAUDE.md` for the original
camera setup notes (that file predates the rest of the game and only documents this camera layer).

---

## Compat

[`Compat/IsExternalInit.cs`](../Assets/Compat/IsExternalInit.cs) — a polyfill that lets the codebase use
C# `init`-only setters / records on the project's compiler target. No runtime behaviour.
