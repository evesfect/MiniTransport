# 04 — Company, Roles & Management Subsystems

These are the per-role gameplay systems: the shared economy plus each manager role's domain. All follow
the server-authoritative + JSON-sync pattern from [01](01-architecture.md). The five player roles and
the systems they primarily drive:

| Role | Primary systems | Key panels |
|------|-----------------|-----------|
| **General Manager** | fares & satisfaction, final approvals | GM panel |
| **Transport Manager** | routes, bus scheduling | Transport / route panels |
| **Maintenance Manager** | repairs, work queue, thresholds | Maintenance / work-item panels |
| **Finance Manager** | budget approvals, vendors/orders, inventory | Finance / vendor panels |
| **HR Manager** | hiring, training, teams | HR panel |

Roles are assigned by [`RoleManager`](#rolemanager) and gate UI via
[`RequestButtonAccess`](06-ui.md).

---

## CompanyManager (money, ledger, satisfaction, fares)

[`Assets/Scripts/Managers/CompanyManager.cs`](../Assets/Scripts/Managers/CompanyManager.cs) — the shared
economy every other system bills against. Persists to `company.json`.

- **Balance & ledger.** `CompanyData` holds `CurrentBalance`, a cumulative `TransferTripCount` KPI, and
  a `History` of `Transaction`s (`{Amount, Type, Category, Description, Timestamp(day), Count}`).
  Transactions are tagged `Actionable` (player-initiated purchase) vs `Passive` (automatic bill) and a
  `TransactionCategory` (TicketRevenue, StaffSalary, PartPurchase, Tax, …).
- **Transaction API:**
  - `TryExecuteActionableTransaction(amount, category, desc)` — a guarded spend that **fails** (returns
    false) if it would push the balance below `bankruptcyThreshold`. This floors player purchases so the
    only way to go bankrupt is unavoidable passive bills.
  - `ProcessPassiveExpense(...)` / `AddIncome(...)` — unguarded bills and revenue.
  - **Daily aggregation:** repeated same-day/same-category transactions are merged into one ledger line
    with a `Count`, keeping the history compact.
- **Weekly billing signal.** Listens to `OnDayChanged`; on `day % 7 == 0` raises
  `OnWeeklyExpensesRequested`. Fleet tax, payroll, staff upkeep, and weekly vendor refresh all subscribe
  to it.
- **Satisfaction.** `GlobalSatisfaction` (0–100) is rewarded when passengers reach their destination and
  penalized when they give up waiting. Mirrored to clients via a `NetworkVariable`.
- **Fares (GM-tunable).** `TicketPrice` and `TransferDiscount` are `NetworkVariable`s set by the GM panel
  through `RequestSetTicketPriceRpc` / `RequestSetTransferDiscountRpc`; `BusDriver` reads them when
  charging fares.
- **Networked mirrors.** Latest-expense (desc+amount), ticket price, transfer discount, and satisfaction
  are all `NetworkVariable`s so the HUD/GM panel work on clients without subscribing to the full ledger.
- **Sync:** stats & ledger sync through `NetworkSyncBroker` (`CompanyStats` @0.5s, `CompanyLedger` @1s).
- **Save throttling.** Writes are buffered behind a 5s timer and swallow transient file-lock
  `IOException`s (see [01 §8](01-architecture.md#8--save--load)).

---

## FleetManager (the buses)

[`Assets/Scripts/Managers/FleetManager.cs`](../Assets/Scripts/Managers/FleetManager.cs) — master list of
all [`BusData`](../Assets/Scripts/Data/BusDataTypes.cs). Persists to `fleet.json`.

- **`BusData`:** `BusID`, `AssignedDepotID`, a `BusSchedule` (route + start/end hours + turnaround),
  `PendingSale`, `Capacity`, and a list of [`BusPartData`] (Engine, Transmission, Tires, Body, Interior;
  each with `Health` 0–100 and a degrading `MaxLife` ceiling). `GetAverageHealth()` summarizes condition.
- **CRUD:** `CreateBusClient/UpdateBusClient/DeleteBusClient` → `RequestFleetOperationRpc(Add/Update/Remove)`
  → broadcast full fleet via `SyncFleetRpc`. Raises `OnFleetUpdated`.
- **Runtime instance map (server-only):** `RegisterSpawnedBus`/`UnregisterBus`/`GetActiveBus` map a
  `BusID` to its live spawned GameObject. The count of active buses is mirrored to clients via a
  `NetworkVariable` (`ActiveBusCount`).
- **Part mutation:** `UpdateBusPartHealth` / `UpdateBusPartMaxLife` (called by maintenance) clamp values
  and mark fleet/maintenance stats dirty.
- **Billing:** subscribes to `OnWeeklyExpensesRequested` → weekly tax = `buses × weeklyCostPerBus`.

---

## MaintenanceManager (wear, breakdowns, repairs, work queue)

[`Assets/Scripts/Managers/MaintenanceManager.cs`](../Assets/Scripts/Managers/MaintenanceManager.cs) — the
Maintenance Manager's domain. Drives part decay, breakdowns, and the repair pipeline. Server-only logic
hung off the time events.

- **Decay (`OnMinuteChanged`).** Each *active* (driving) bus's parts lose `Health` (and slowly `MaxLife`,
  the permanent ceiling) per minute, scaled by a per-part multiplier (tires wear faster, body slower).
  When a **critical** part (Engine/Transmission/Tires) drops to `breakdownThreshold`, `TriggerBreakdown`
  flags the bus broken (→ `BusDriver.SetBrokenDown`), enqueues a `WorkItem`, and dispatches field
  recovery.
- **Depot repairs (`OnHourChanged`).** Parked buses are repaired by **mechanic teams**. Mechanics
  assigned to a depot are grouped into teams (`DepotID_TeamID`) whose pooled skill is the team's hourly
  *capacity*. For each parked bus needing work, an idle team in that depot is assigned and spends capacity
  fixing parts in `repairPriority` order, respecting a per-part max allowance. Two operations:
  - **Repair:** raise `Health` toward `MaxLife` (rate = capacity × `repairPerSkillPoint`).
  - **Replace:** if `MaxLife` fell below `replacePartThreshold`, the team demands a specific spare
    `ItemID` and consumes it from `InventoryManager` (restoring `MaxLife`/`Health`); if out of stock the
    bus stalls "awaiting parts."
- **Work queue.** `WorkItem`s (`AwaitingTechnician / AwaitingParts / InRepair`, with priority) are the UI
  model for the Maintenance dashboard. `PrioritizeWorkItem`, `ReorderWorkQueue`, etc. let the player
  reorder; `OnWorkQueueChanged` refreshes the UI.
- **Settings (networked):** `operationalThreshold` (min health to leave the depot), `breakdownThreshold`,
  `replacePartThreshold`, and the `repairPriority` order — all settable from the dashboard via RPC.
- **KPI hooks:** raises `OnBreakdownOccurred / OnRepairCompleted / OnPartReplaced` and tracks MTTR
  (mean-time-to-repair), technician utilization, spare-part delay episodes, and breakdowns resolved —
  all consumed by `KPIManager`.

---

## EmployeeManager (HR: hiring, training, teams, fatigue)

[`Assets/Scripts/Managers/EmployeeManager.cs`](../Assets/Scripts/Managers/EmployeeManager.cs) — the HR
Manager's domain. Persists to `employees.json`. Today the only role is **Mechanic**.

- **`EmployeeData`:** id, name, role, `SkillLevel` (0–100, drives wage & repair capacity), `WeeklySalary`,
  depot/team assignment, `TrainingDaysRemaining`, and `Fatigue` (with `Morale = 100 − Fatigue`).
- **Recruitment campaigns.** `LaunchAdCampaign(AdTier)` (Flyers/Classifieds/Headhunter) costs money and,
  *the next morning* (`OnDayChanged` → `ProcessPendingAdCampaign`), delivers a random pool of candidates
  whose skill distribution depends on the tier. Campaign state is synced so the HR banner is correct on
  all peers.
- **Actions:** `HireCandidate(index)` (pays fee + wage, moves candidate → employee, notifies
  `RequestManager`), `FireEmployee(id)`, `TrainEmployee(id, days)` (pays upfront; the mechanic is *away*
  and gains skill each day via `ProcessTraining`), `AssignMechanicToDepot/Team`.
- **Auto-teaming.** `AutoAssignMechanicsToTeams` packs mechanics into teams targeting a minimum pooled
  skill (so each team can handle at least an engine repair), preserving any custom teams from a save.
- **Fatigue (`OnHourChanged`).** Assigned mechanics tire during work hours; everyone else (off-hours,
  idle, in training) recovers. Surfaces as the HR `avgFatigue` KPI.
- **Payroll.** On `OnWeeklyExpensesRequested`: total salaries + per-head upkeep billed to
  `CompanyManager`.

---

## VendorManager (procurement)

[`Assets/Scripts/Managers/VendorManager.cs`](../Assets/Scripts/Managers/VendorManager.cs) — the Finance
Manager's parts supply chain. Persists to `vendors.json`.

- **Vendors.** Each week (`OnWeeklyExpensesRequested`) a fresh market of `VendorData` is generated, three
  quality tiers (`Low/Mid/High`) per part category (Engine/Tires/Chassis/Electronics). Quality tier sets
  reliability, delivery speed, price, and durability range. Vendors under an active deal carry over.
- **Deals.** `SignDeal(vendorID, category)` locks in a supplier (max 2 per category); cancelling early
  (<7 days) incurs a fine.
- **Orders.** `PlaceOrder(vendorID, baseItemName, amount)` charges immediately, rolls a delivery time
  (with a reliability-based chance of being **delayed**), and a durability value. Item IDs are generated
  with running per-item counters so each delivered part has a unique display ID (e.g. `Tire5-12`).
- **Delivery (`OnHourChanged` → `ProcessActiveOrders`).** When an order's arrival hour passes, parts are
  added to `InventoryManager` with their rolled durability; vendors earn loyalty XP (raising loyalty
  level, lowering price). Order placement notifies `RequestManager` (fulfilling BuyParts requests).
- **KPI counters:** lifetime orders placed/delivered/on-time and a quality sum feed the Finance & Vendor
  reports.

---

## InventoryManager (the parts warehouse)

[`Assets/Scripts/Managers/InventoryManager.cs`](../Assets/Scripts/Managers/InventoryManager.cs) — stock of
spare parts. Persists to `inventory.json`.

- **Model:** `Dictionary<itemID, List<PartCondition>>` — each physical part instance carries its own
  durability, so a stack of "Tire" parts can have varied quality (FIFO consumption).
- **Item definitions** are [`InventoryItemData`](../Assets/Scripts/Data/InventoryItemData.cs)
  ScriptableObjects auto-loaded from `Resources/InventoryItems`.
- **API:** `AddPartWithDurability` (used by vendor deliveries), `IncreaseItemQuantity`,
  `DecreaseItemQuantity`, `TryConsumeItem(itemID, out durability)` (used by maintenance replacements —
  pulls the first part, returns its durability), `GetItemQuantity`. Mutations resync the full inventory
  JSON and raise `OnItemQuantityChanged` for the UI.

---

## RequestManager (cross-role workflow)

[`Assets/Scripts/Managers/RequestManager.cs`](../Assets/Scripts/Managers/RequestManager.cs) — the
inter-role approval system; how one manager asks another to do something. Persists to `requests.json`.

- **`GameRequest`** ([`Data/RoleAndRequestData.cs`](../Assets/Scripts/Data/RoleAndRequestData.cs)):
  `{Type, Requester, CurrentTarget, TargetAmount, CurrentAmount, Payload(JSON/string), State,
  RejectReason}`. Types: `HireMechanic, TrainMechanic, BuyParts, BuyBus, SellBus`. States: `Active,
  AwaitingGeneralManager, Completed, Rejected, Read`. `GetNotificationText()` renders the friendly
  notification string.
- **Creation:** `CreateRequest(type, target, amount, payload)` — e.g. Maintenance asks Finance to buy
  parts.
- **Automatic progress:** other managers call `NotifyActionTaken(type, amount, condition)` when they act
  (hire/train/order). The request matches by payload conditions (min skill for hires, item ID for parts,
  employee IDs for training) and advances `CurrentAmount` until complete.
- **Two-tier approval (money):** BuyBus/SellBus flow **Requester → Finance → GM**. `ApproveForwardRequest`
  moves Finance-approved requests to `AwaitingGeneralManager`; the GM's approval triggers
  `ExecuteGMApproval`, which actually charges the company and adds/removes buses in `FleetManager` (selling
  an *active* bus flags it `PendingSale` to sell on return). `RejectRequest`, `MarkAsRead` manage the
  rest of the lifecycle.

---

## RoleManager

[`Assets/Scripts/Managers/RoleManager.cs`](../Assets/Scripts/Managers/RoleManager.cs) — maps each
connected `clientId` to a `PlayerRole`. `SelectRole` claims a role (rejected by the server if already
taken); disconnects free the role. `GetMyRole()`, `IsRoleTaken(role)`, and `OnRolesUpdated` drive the
role picker and UI gating. `RoleToReport(role)` maps a role to the end-of-game report it should see.

---

## GameEndManager

[`Assets/Scripts/Managers/GameEndManager.cs`](../Assets/Scripts/Managers/GameEndManager.cs) —
server-authoritative game-over controller. Watches two conditions: **bankruptcy** (balance below
`CompanyManager.bankruptcyThreshold`) and **time limit** (`CurrentDay > gameLengthDays`). When either
fires, it sets networked `_gameOver`/`_reason`, calls `SimulationTimeManager.LockTime()` (freeze the
clock), and `KPIManager.FinalizeReports()` (push final report snapshots). Every peer receives the result
via `NetworkVariable`s; the client `GameOverScreen` reveals the role's report. Handles late joiners.
