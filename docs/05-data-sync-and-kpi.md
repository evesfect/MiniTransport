# 05 — Data Sync Layer & KPI Reports

Two cooperating subsystems sit between the authoritative managers and the UI:

1. The **interest-based sync layer** — delivers live dashboard data to only the clients who are looking,
   rate-limited.
2. **`KPIManager`** — aggregates end-of-game report metrics from events the managers already raise.

---

## The sync layer

Three pieces, one on the wire and two local:

```
 UI panel
   │ RegisterInterest(type) / UnregisterInterest(type)
   ▼
 LocalDataBroker (per peer)  ──client only──▶  NetworkSyncBroker (server)
   │ hooks local manager events                  │ tracks subscribers, rate-limits,
   │ pushes values into                          │ fires per-type sync events when dirty
   ▼                                             ▼
 ClientDataCache (ScriptableObject)  ◀──RPC snapshots──  managers' Perform*Sync handlers
   │ raises C# events
   ▼
 UI panel updates
```

### SyncDataType

[`Assets/Scripts/Data/SyncDataTypes.cs`](../Assets/Scripts/Data/SyncDataTypes.cs) defines a `[Flags]`
enum of subscribable channels — live dashboards (`CompanyStats`, `FleetStats`, `MaintenanceStats`,
`CompanyLedger`) and the six end-of-game report snapshots (`GeneralReport`, `OperationsReport`,
`MaintenanceReport`, `HrReport`, `FinanceReport`, `VendorReport`). The file also defines the flat
value structs each channel ships (e.g. `CompanyStatsData`, `MaintenanceReportData`, …) — kept to
ints/floats so JSON payloads stay tiny.

### NetworkSyncBroker (server-side throttler)

[`Assets/Scripts/Systems/NetworkSyncBroker.cs`](../Assets/Scripts/Systems/NetworkSyncBroker.cs)
(`[DefaultExecutionOrder(-10)]`, singleton).

- Tracks **per-type subscriber sets** (`clientId`s) populated by `SubscribeRpc`/`UnsubscribeRpc`.
- Managers call `MarkDirty(type)` when their data changes. Each type has a **rate limit** (company stats
  0.5s, ledger 1s, fleet 2s, maintenance 1s, reports 5s). The `Update` loop, for each dirty type whose
  timer has elapsed and that has subscribers, fires a C# event (`OnCompanySyncTriggered`,
  `OnReportSyncTriggered(type, target)`, …) carrying a `BaseRpcTarget` group of exactly those subscribers.
- The owning manager listens to that event and sends the actual snapshot RPC only to the target group.
  A new subscriber is immediately sent the current state on subscribe.

Net effect: **no traffic when nobody's subscribed**, and bounded traffic otherwise, all without managers
knowing who is watching.

### LocalDataBroker (per-peer adapter)

[`Assets/Scripts/Systems/LocalDataBroker.cs`](../Assets/Scripts/Systems/LocalDataBroker.cs) — implements
[`ILocalDataProvider`](../Assets/Scripts/Managers/ILocalDataProvider.cs) (`RegisterInterest` /
`UnregisterInterest`). It **reference-counts** interest per type so multiple panels can subscribe
independently.

- On a **client**, the first interest in a type sends `SubscribeRpc` to the server broker.
- On **both** host and client, it hooks the relevant local manager event (e.g.
  `CompanyManager.OnBalanceChanged`, `KPIManager.OnReportsUpdated`) and, whenever it fires, calls
  `PushCurrentState(type)` to write the latest value into the `ClientDataCache`. On the host this means
  dashboards update directly from live managers with **no network hop**; on a client the cache is filled
  by the incoming sync RPC instead.
- A lightweight `Update` retry re-hooks managers that weren't ready yet, and `ResendSubscriptions()` is
  called when a client finishes connecting so subscriptions survive the connection handshake.

### ClientDataCache (the UI's data source)

[`Assets/Scripts/Data/ClientDataCache.cs`](../Assets/Scripts/Data/ClientDataCache.cs) — a
`ScriptableObject` holding the latest snapshot of each channel and a matching C# `Action` event. UI
panels bind to these events (`OnCompanyDataUpdated`, `OnMaintenanceReportUpdated`, …) and never touch
the network directly. Being an asset, it can be referenced from any scene object in the inspector and
inspected live.

> **Why a ScriptableObject?** It decouples producers (broker/managers) from consumers (UI) — neither
> needs a direct reference to the other, just to the shared asset.

---

## KPIManager (end-of-game reports)

[`Assets/Scripts/Managers/KPIManager.cs`](../Assets/Scripts/Managers/KPIManager.cs)
(`[DefaultExecutionOrder(-40)]`, singleton, server-authoritative).

It is the single aggregator for the six end-of-game reports. Design goals: **zero added per-frame
traffic** and **no polling** — it accumulates counters by subscribing to events the managers already
raise, builds a small flat struct per report on demand, and ships each only to subscribed clients via
the same `NetworkSyncBroker` model.

- **Collection (server).** Subscribes to maintenance (`OnBreakdownOccurred/OnRepairCompleted/
  OnPartReplaced`), passengers (`OnPassengersServed/OnPassengersTimedOut`), company
  (`OnTransferRecorded`), employees (`OnEmployeeHired`), and the clock (`OnHourChanged` to sample fleet
  utilization, `OnDayChanged` as a daily refresh heartbeat). Each handler bumps a lifetime counter and
  marks the affected report(s) dirty.
- **Snapshot builders.** `BuildGeneralReport` / `BuildOperationsReport` / `BuildMaintenanceReport` /
  `BuildHrReport` / `BuildFinanceReport` / `BuildVendorReport` read live values from the relevant
  managers and compute derived metrics (on-time % ≈ served/(served+missed), avg wait, fleet utilization &
  reliability, MTTR, technician utilization, breakdown frequency, payroll, vendor on-time delivery,
  traffic-light system status, …).
- **Server vs client.** On the server `GetXReport()` builds live; on a client it returns the last
  snapshot received over RPC. `OnReportsUpdated` fires on both (host: counters changed; client: RPC
  received) so the report UI refreshes.
- **Sync.** Hooks `NetworkSyncBroker.OnReportSyncTriggered`; `PerformReportSync(type, target)` serializes
  the right report and sends it to subscribers. `FinalizeReports()` (called by `GameEndManager`) forces a
  final refresh of all six.
- **Live mirror.** `DemandMetPercent` (served vs gave-up) is also exposed as a `NetworkVariable` so a live
  panel (e.g. the GM panel) can show it without subscribing to the heavy report snapshots.

> **Known approximations / stubs** (documented in code): On-Time Performance is approximated from
> served-vs-missed passengers; "Number of Transfers" is counted via `CompanyManager.TransferTripCount`
> rather than true per-passenger journey identity. These are flagged in `SyncDataTypes.cs` and
> `KPIManager` comments as future work.

---

## Wiring a new dashboard value

1. Add a flag to `SyncDataType` and a flat value struct (+ getter/setter/event) to `ClientDataCache`.
2. In the producing manager, call `NetworkSyncBroker.MarkDirty(yourType)` on change and add a
   `Perform…Sync(target)` handler that ships a JSON snapshot; subscribe it in `OnNetworkSpawn`.
3. In `NetworkSyncBroker`, give the type a rate limit and a sync event (or reuse the report event).
4. In `LocalDataBroker`, hook the manager's change event under that type in `TryStartProvidingData`/
   `PushCurrentState`.
5. In the UI panel, `RegisterInterest(yourType)` on enable and bind to the cache event.
