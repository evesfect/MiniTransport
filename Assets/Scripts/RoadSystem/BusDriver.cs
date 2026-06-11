using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class BusDriver : VehicleDriver
{
    [Header("Debug")]
    public MarkerSpawner debugMarkerSpawner;
    [Tooltip("Log passenger transfer events (board-for-transfer, alight-to-transfer) to the console.")]
    public bool logTransfers = true;

    // Properties baseSpeed, clientSpeedBuffer, rotationSpeed are in Base Class

    [Header("Network State")]
    private readonly NetworkVariable<BusNetworkState> _netState = new NetworkVariable<BusNetworkState>(
        new BusNetworkState { IsInService = false },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    //Passenger Logic
    // Server-only. A passenger's alight tile (where they leave THIS bus) may differ from
    // their final destination when they are mid-transfer.
    private struct OnboardGroup
    {
        public int AlightTile;     // tile where this group leaves the current bus
        public int FinalDestTile;  // tile the passengers ultimately want to reach
        public ushort Count;
    }
    private List<OnboardGroup> _passengersOnBoard = new List<OnboardGroup>();


    // Breakdown logic
    public BusPartType BreakdownReason => _netState.Value.BreakdownReason;

    public int PassengerCount => _netState.Value.PassengerCount;

    private bool IsShiftActive
    {
        get
        {
            if (_serverEntry == null || _serverEntry.Schedule == null) return false;
            float now = SimulationTimeManager.Instance.CurrentTimeOfDay;
            return now >= _serverEntry.Schedule.StartTime && now < _serverEntry.Schedule.EndTime;
        }
    }
    public bool IsBroken => _netState.Value.IsBrokenDown;
    public string PreviousStopID => _netState.Value.PreviousStopID.ToString();
    public string TargetStopID => _netState.Value.TargetStopID.ToString();
    public float GetBreakdownDist() => _netState.Value.BreakdownStopDistance;
    public bool IsFullyStopped => IsBroken && (m_ServerDistanceTraveled >= _netState.Value.BreakdownStopDistance);
    private bool _hasNotifiedStop = false;
    private float _breakdownBuffer = 2.0f;

    

    public float RemainingPathDistance => Mathf.Max(0f, m_ServerCurrentLegLength - m_ServerDistanceTraveled);

    // Server Side Data ()
    private BusData _serverEntry;
    private DepotController _serverDepot;
    private Route _serverRoute;
    private int _serverRouteIndex;
    
    // Server Ghost Simulation (: Waiting at stops)
    private bool _serverIsWaiting;
    private float _serverWaitTimer;

    // Client Side Simulation
    // PathLeg struct and Lists moved to Base Class (m_ServerPathSegments, m_LocalPathSegments, etc)

    public override void OnNetworkSpawn()
    {
        _netState.OnValueChanged += OnNetworkStateChanged;

        if (_netState.Value.IsInService)
        {
            OnNetworkStateChanged(default, _netState.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        _netState.OnValueChanged -= OnNetworkStateChanged;
    }

    // Initialization requires BusData and Depot
    public void ServerInitialize(BusData entry, DepotController depot)
    {
        if (!IsServer) return;

        _serverEntry = entry;
        _serverDepot = depot;
        _serverRoute = TransportManager.Instance.GetRoute(entry.Schedule.RouteID);

        if (_serverRoute == null) { DespawnBus(); return; }

        _serverRouteIndex = 0; 
        
        _serverIsWaiting = true;
        _serverWaitTimer = 0.5f; 
        
        string firstStop = _serverRoute.StopIDs[0];
        
        BusNetworkState initState = new BusNetworkState
        {
            CurrentRouteID = _serverRoute.RouteID,
            PreviousStopID = firstStop,
            TargetStopID = firstStop, 
            DepartureTime = SimulationTimeManager.Instance.CurrentTimeOfDay,
            IsReverseDirection = false,
            IsInService = true
        };
        _netState.Value = initState;

        
    }

    private void Update()
    {
        if (IsServer) ServerUpdateLoop();
        if (IsClient) ClientUpdateLoop();
    }

    private void ServerUpdateLoop()
    {
        // Check Service/Broken status
        if (!_netState.Value.IsInService) return;
        if (_netState.Value.IsBrokenDown)
        {
            if (m_ServerDistanceTraveled >= _netState.Value.BreakdownStopDistance)
            {
                m_ServerDistanceTraveled = _netState.Value.BreakdownStopDistance;

                if (!_hasNotifiedStop)
                {
                    _hasNotifiedStop = true;
                    if (MaintenanceManager.Instance != null)
                    {
                        MaintenanceManager.Instance.OnBusStopped(_serverEntry.BusID);
                    }
                }
                return; // Bus is fully stopped at breakdown dist
            }
        }
        float dt = Time.deltaTime * SimulationTimeManager.Instance.TimeMultiplier;

        if (_serverIsWaiting)
        {
            _serverWaitTimer -= (dt * SimulationTimeManager.Instance.baseMinutesPerSecond) / 60f; 
            
            if (_serverWaitTimer <= 0)
            {
                ServerStartNextLeg();
            }
        }
        else
        {
            float trafficModifier = 1.0f; 
            
            // Uses base class path list (m_ServerPathSegments)
            if (GridManager.Instance != null && m_ServerPathSegments.Count > 0)
            {
                // Uses base class math (CalculatePoint)
                Vector3 serverPos = CalculatePoint(m_ServerDistanceTraveled, m_ServerPathSegments, out _);
                trafficModifier = GridManager.Instance.GetTrafficModifierAt(serverPos);
            }

            float step = baseSpeed * trafficModifier * dt;
            m_ServerDistanceTraveled += step;

            // Cap movement if breaking down
            if (_netState.Value.IsBrokenDown && m_ServerDistanceTraveled >= _netState.Value.BreakdownStopDistance)
            {
                m_ServerDistanceTraveled = _netState.Value.BreakdownStopDistance;
            }

            // normal stop arrival
            if (m_ServerDistanceTraveled >= m_ServerCurrentLegLength)
            {
                ServerArriveAtStop();
            }
        }
    }

    // Maintenance Logic
    public void SetBrokenDown(bool isBroken, BusPartType reason = BusPartType.None)
    {
        if (!IsServer) return;
        var state = _netState.Value;
        
        if (isBroken && !state.IsBrokenDown)
        {
            float bufferDistance = baseSpeed * _breakdownBuffer;
            float targetDist = m_ServerDistanceTraveled + bufferDistance;

            if (targetDist > m_ServerCurrentLegLength) targetDist = m_ServerCurrentLegLength;
        
            state.BreakdownStopDistance = targetDist;
            state.IsBrokenDown = true;
            state.BreakdownReason = reason;
            _hasNotifiedStop = false;
        }
        else if (!isBroken)
        {
            state.BreakdownStopDistance = 0f;
            state.IsBrokenDown = false;
            state.BreakdownReason = BusPartType.None;
            _hasNotifiedStop = false;
        }
        _netState.Value = state;
    }

    private void ServerStartNextLeg()
    {
        

        // Schedule Check
        if (!IsShiftActive)
        {
            if (_passengersOnBoard.Count == 0)
            {
                DespawnBus();
                return;
            }
            
        }

        var state = _netState.Value;
        int nextIndex = _serverRouteIndex + (state.IsReverseDirection ? -1 : 1);

        if (nextIndex >= _serverRoute.StopIDs.Count || nextIndex < 0)
        {
            if (_serverRoute.StopIDs.First() == _serverRoute.StopIDs.Last())
            {
                nextIndex = (nextIndex >= _serverRoute.StopIDs.Count) ? 1 : _serverRoute.StopIDs.Count - 2;
            }
            else
            {
                state.IsReverseDirection = !state.IsReverseDirection;
                nextIndex = _serverRouteIndex + (state.IsReverseDirection ? -1 : 1);
            }
        }

        string fromID = _serverRoute.StopIDs[_serverRouteIndex];
        string toID = _serverRoute.StopIDs[nextIndex];
        _serverRouteIndex = nextIndex;

        BusStop fromStop = TransportManager.Instance.GetStop(fromID);
        BusStop toStop = TransportManager.Instance.GetStop(toID);

        if (fromStop && toStop)
        {
            // Uses Bus-Specific logic to find nodes, then fills Generic m_ServerPathSegments
            BuildPathSegments(fromStop, toStop, m_ServerPathSegments, out m_ServerCurrentLegLength);
        }
        else
        {
            m_ServerPathSegments.Clear();
            m_ServerCurrentLegLength = 10f; // Fallback
        }

        m_ServerDistanceTraveled = 0f;
        _serverIsWaiting = false;

        state.PreviousStopID = fromID;
        state.TargetStopID = toID;
        state.DepartureTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
        
        _netState.Value = state;
    }

    private void ServerArriveAtStop()
    {
        _serverIsWaiting = true;
        int passengersMoved = 0;

        BusStop currentStop = TransportManager.Instance.GetStop(_serverRoute.StopIDs[_serverRouteIndex]);
        if (currentStop != null)
        {
            // This now returns the total number of people boarding/leaving
            passengersMoved = ServerHandlePassengers(currentStop);
        }

        // DYNAMIC WAIT CALCULATION
        // Formula: Minimum base time + (interactions * time per person)
        float dynamicWaitMinutes = baseWaitTime + (passengersMoved * timePerPassenger);

        // If it's a major terminus, ensure we respect the scheduled turnaround
        bool isTerminus = _serverRouteIndex == 0 || _serverRouteIndex == _serverRoute.StopIDs.Count - 1;
        if (isTerminus && _serverEntry.Schedule.TurnaroundWait > dynamicWaitMinutes)
        {
            dynamicWaitMinutes = _serverEntry.Schedule.TurnaroundWait;
        }

        // Convert game minutes to real-world countdown seconds for the update loop
        _serverWaitTimer = dynamicWaitMinutes / 60f;
    }

    private void DespawnBus()
    {
        // Check if the bus was flagged for sale while it was out driving
        if (_serverEntry != null && _serverEntry.PendingSale)
        {
            Debug.Log($"[BusDriver] Bus {_serverEntry.BusID} returned to depot and is now being sold!");
            
            // Trigger the actual sale/removal
            FleetManager.Instance.RequestFleetOperationRpc(JsonUtility.ToJson(new BusData { BusID = _serverEntry.BusID }), FleetManager.FleetOperation.Remove);
            
            // Grant the income now that the shift is over
            if (CompanyManager.Instance != null)
            {
                CompanyManager.Instance.AddIncome(8000f, TransactionCategory.VehiclePurchase, $"Sold bus {_serverEntry.BusID} (Delayed)");
            }
            
            return; // Stop here, do not return it to the normal depot cycle
        }

        // Normal behavior: park the bus
        if(_serverDepot != null) _serverDepot.ReturnBusToDepot(_serverEntry.BusID);
    }

    //Passenger Pickup Logic
    // Inside BusDriver.cs

    [Header("Simulation Settings")]
    public float baseWaitTime = 2.0f; // Minimum minutes to wait
    public float timePerPassenger = 0.2f; // Minutes added per passenger boarding/alighting

    private int ServerHandlePassengers(BusStop currentStop)
    {
        int totalInteractions = 0;
        if (GridManager.Instance == null) return 0;

        // 1. Get current Tile Index
        if (!GridManager.Instance.WorldToGrid(currentStop.transform.position, out int x, out int y)) return 0;
        int currentTileIndex = GridManager.Instance.GetIndex(x, y);

        // Snapshot the stop's waiting groups BEFORE drop-off, so passengers who alight here
        // to transfer are not immediately re-boarded onto this same bus on this visit.
        List<WaitingPassengerGroup> waitingSnapshot = null;
        var liveWaiting = PassengerManager.Instance.GetPassengersAtStop(currentStop.stopID);
        if (liveWaiting != null) waitingSnapshot = new List<WaitingPassengerGroup>(liveWaiting);

        // 2. DROP OFF (Alighting + transfers)
        // Passengers leave the bus at their planned alight tile. Each one adds to the timer.
        int arrivedCount = 0; // passengers who reached their FINAL destination here
        for (int i = _passengersOnBoard.Count - 1; i >= 0; i--)
        {
            if (_passengersOnBoard[i].AlightTile != currentTileIndex) continue;

            var group = _passengersOnBoard[i];
            totalInteractions += group.Count;
            _passengersOnBoard.RemoveAt(i);

            if (group.FinalDestTile == currentTileIndex)
            {
                // Reached final destination.
                arrivedCount += group.Count;
            }
            else
            {
                // Transfer point: passengers wait here for the next leg toward their
                // destination. AddPassengers resets their patience and syncs the waiting UI.
                PassengerManager.Instance.AddPassengers(currentStop.stopID, group.FinalDestTile, group.Count);
                if (CompanyManager.Instance != null)
                    CompanyManager.Instance.RecordTransfer(group.Count);

                if (logTransfers)
                {
                    int kpiTotal = CompanyManager.Instance != null ? CompanyManager.Instance.GetCompanyData().TransferTripCount : 0;
                    Debug.Log($"<color=orange>[Transfer]</color> {group.Count} pax ALIGHTED to transfer at stop {currentStop.stopID} " +
                              $"(tile {currentTileIndex}) off bus {BusIdLabel()} [route {RouteLabel()}] — continuing toward tile {group.FinalDestTile}. " +
                              $"(Total Transfer Trips: {kpiTotal})");
                }
            }
        }

        if (arrivedCount > 0 && CompanyManager.Instance != null)
        {
            float baseReward = arrivedCount * CompanyManager.Instance.baseRewardPerPassenger;

            // NEW SATISFACTION CALCULATION: Derived from the Bus's Interior Health condition
            float interiorHealthPct = 1.0f;
            if (_serverEntry != null && _serverEntry.Parts != null)
            {
                var interiorPart = _serverEntry.Parts.FirstOrDefault(p => p.PartType == BusPartType.Interior);
                if (interiorPart != null)
                {
                    interiorHealthPct = Mathf.Clamp01(interiorPart.Health / 100f);
                }
            }

            // Map interior health (0.0 to 1.0) to a satisfaction multiplier (0.7x to 1.2x)
            float conditionMultiplier = 0.7f + (interiorHealthPct * 0.5f);
            float finalReward = baseReward * conditionMultiplier;

            CompanyManager.Instance.ModifySatisfaction(finalReward);
        }

        // 3. PICK UP (Boarding) - transfer-aware planning

        if (IsShiftActive && waitingSnapshot != null)
        {
            int myCapacity = _serverEntry.Capacity; // Unique capacity from BusData
            int currentLoad = GetTotalPassengerCount();

            if (currentLoad < myCapacity && RouteNetworkGraph.Instance != null)
            {
                // Tiles this bus will reach next, in arrival order, for transfer planning.
                List<int> upcomingTiles = GetUpcomingTilesInOrder();

                foreach (var group in waitingSnapshot)
                {
                    if (currentLoad >= myCapacity) break;

                    int finalDest = group.DestinationTileIndex;

                    // Board only if this bus brings the passenger strictly closer (in rides)
                    // to their destination. alightTile is where they leave THIS bus.
                    if (RouteNetworkGraph.Instance.TryPlanLeg(currentTileIndex, upcomingTiles, finalDest, out int alightTile))
                    {
                        int space = myCapacity - currentLoad;
                        ushort countToTake = (ushort)Mathf.Min(space, group.PassengerCount);

                        if (countToTake > 0)
                        {
                            // Add to bus storage and remove from the physical stop
                            AddToBusStorage(alightTile, finalDest, countToTake);
                            PassengerManager.Instance.RemovePassengers(currentStop.stopID, finalDest, countToTake);

                            currentLoad += countToTake;
                            totalInteractions += countToTake;

                            // Only log transfer legs (alight tile differs from final destination);
                            // ordinary direct boardings are left unlogged to avoid console spam.
                            if (logTransfers && alightTile != finalDest)
                            {
                                Debug.Log($"<color=orange>[Transfer]</color> {countToTake} pax BOARDED bus {BusIdLabel()} [route {RouteLabel()}] " +
                                          $"at stop {currentStop.stopID} (tile {currentTileIndex}) — will alight at tile {alightTile} to transfer toward final tile {finalDest}.");
                            }
                        }
                    }
                }
            }
        }


        // Update Network State for Client UI
        var state = _netState.Value;
        state.PassengerCount = (ushort)GetTotalPassengerCount();
        _netState.Value = state;

        return totalInteractions;
    }

    // Helper: Simulate the route forward to list the upcoming stop tiles in arrival order
    // (de-duplicated, keeping first occurrence). Used for transfer planning.
    private List<int> GetUpcomingTilesInOrder()
    {
        List<int> ordered = new List<int>();
        HashSet<int> seen = new HashSet<int>();

        if (_serverRoute == null || _serverRoute.StopIDs.Count < 2) return ordered;
        if (GridManager.Instance == null) return ordered;

        // Start simulation from current state
        int simIndex = _serverRouteIndex;
        bool simDirection = _netState.Value.IsReverseDirection;
        int stopsToCheck = _serverRoute.StopIDs.Count * 2; // Limit lookahead to prevent infinite loops (e.g. 1 full round trip)

        for (int i = 0; i < stopsToCheck; i++)
        {
            // --- SIMULATE MOVEMENT LOGIC (Copied from ServerStartNextLeg) ---
            int nextIndex = simIndex + (simDirection ? -1 : 1);

            if (nextIndex >= _serverRoute.StopIDs.Count || nextIndex < 0)
            {
                if (_serverRoute.StopIDs.First() == _serverRoute.StopIDs.Last())
                {
                    // Circular Logic: Jump to 1 or Count-2 to maintain loop
                    nextIndex = (nextIndex >= _serverRoute.StopIDs.Count) ? 1 : _serverRoute.StopIDs.Count - 2;
                }
                else
                {
                    // Ping-Pong Logic: Flip direction
                    simDirection = !simDirection;
                    nextIndex = simIndex + (simDirection ? -1 : 1);
                }
            }

            // Update simulation index
            simIndex = nextIndex;

            // --- GET TILE FOR THIS STOP ---
            string stopID = _serverRoute.StopIDs[simIndex];
            BusStop stop = TransportManager.Instance.GetStop(stopID);

            if (stop != null)
            {
                if (GridManager.Instance.WorldToGrid(stop.transform.position, out int x, out int y))
                {
                    int tileIndex = GridManager.Instance.GetIndex(x, y);
                    if (seen.Add(tileIndex)) ordered.Add(tileIndex);
                }
            }
        }

        return ordered;
    }

    private void AddToBusStorage(int alightTile, int finalDest, ushort count)
    {
        for (int i = 0; i < _passengersOnBoard.Count; i++)
        {
            if (_passengersOnBoard[i].AlightTile == alightTile && _passengersOnBoard[i].FinalDestTile == finalDest)
            {
                var g = _passengersOnBoard[i];
                g.Count += count;
                _passengersOnBoard[i] = g;
                return;
            }
        }
        // New group
        _passengersOnBoard.Add(new OnboardGroup { AlightTile = alightTile, FinalDestTile = finalDest, Count = count });
    }

    private int GetTotalPassengerCount()
    {
        int total = 0;
        foreach (var g in _passengersOnBoard) total += g.Count;
        return total;
    }

    // Null-safe labels for transfer logging.
    private string BusIdLabel() => _serverEntry != null ? _serverEntry.BusID : "?";
    private string RouteLabel() => _serverRoute != null ? _serverRoute.RouteName : "?";

    // Client Logic (Visuals)
    private void OnNetworkStateChanged(BusNetworkState oldState, BusNetworkState newState)
    {
        if (!newState.IsInService) return;

        BusStop from = TransportManager.Instance.GetStop(newState.PreviousStopID.ToString());
        BusStop to = TransportManager.Instance.GetStop(newState.TargetStopID.ToString());

        if (from != null && to != null)
        {
            if (from == to)
            {
                m_ClientIsMoving = false;
                transform.position = from.transform.position;
                transform.rotation = from.transform.rotation;
                return;
            }

            bool isNewLeg = (oldState.PreviousStopID != newState.PreviousStopID) ||
                        (oldState.TargetStopID != newState.TargetStopID) ||
                        (oldState.DepartureTime != newState.DepartureTime);
            if (!isNewLeg) return;

            BuildPathSegments(from, to, m_LocalPathSegments, out m_TotalLegLength);
            
            float currentGameTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
            float timePassedGameHours = currentGameTime - newState.DepartureTime;
            if (timePassedGameHours < 0) timePassedGameHours += 24f;

            float timeMult = SimulationTimeManager.Instance.TimeMultiplier > 0 ? SimulationTimeManager.Instance.TimeMultiplier : 1f;
            float realSecondsPassed = (timePassedGameHours * 60f) / (SimulationTimeManager.Instance.baseMinutesPerSecond * timeMult);

            m_ClientDistanceTraveled = realSecondsPassed * baseSpeed * clientSpeedBuffer;
            m_ClientIsMoving = true;
        }
    }

    private void ClientUpdateLoop()
    {
        // Check Broken status
        if (!m_ClientIsMoving || m_LocalPathSegments == null || m_LocalPathSegments.Count == 0) return;

        if (_netState.Value.IsBrokenDown)
        {
            if (m_ClientDistanceTraveled >= _netState.Value.BreakdownStopDistance)
            {
                // Snap to exact stopping point to align visually with Server/Recovery Vehicle
                m_ClientDistanceTraveled = _netState.Value.BreakdownStopDistance;
                UpdateTransformOnSpline(m_ClientDistanceTraveled, m_LocalPathSegments);
                return; // Stop moving
            }
        }

        float dt = Time.deltaTime * SimulationTimeManager.Instance.TimeMultiplier;

        // TRAFFIC CHECK (Generic concept, but logic kept here for flow control)
        float localTraffic = 1.0f;
        if (GridManager.Instance != null)
        {
            localTraffic = GridManager.Instance.GetTrafficModifierAt(transform.position);
        }

        float step = baseSpeed * localTraffic * clientSpeedBuffer * dt;
        
        m_ClientDistanceTraveled += step;

        if (_netState.Value.IsBrokenDown && m_ClientDistanceTraveled >= _netState.Value.BreakdownStopDistance)
        {
            m_ClientDistanceTraveled = _netState.Value.BreakdownStopDistance;
            // keep clamping until breakdown is cleared
        }


        // normal leg completion
        if (m_ClientDistanceTraveled >= m_TotalLegLength)
        {
            m_ClientDistanceTraveled = m_TotalLegLength;
            m_ClientIsMoving = false;
        }

        // Update visuals
        UpdateTransformOnSpline(m_ClientDistanceTraveled, m_LocalPathSegments);
    }

    // Path building strategy (Stop to Stop)
    private void BuildPathSegments(BusStop from, BusStop to, List<PathLeg> targetList, out float totalLength)
    {
        targetList.Clear();
        totalLength = 0f;

        var nodes = TransportManager.Instance.GetPath(from, to);
        
        // 1. Handle Direct/Short Paths
        if (nodes == null || nodes.Count == 0)
        {
            if (from.parentSegment == to.parentSegment && from.parentSegment != null)
            {
                // Uses Base Class helper AddPathLeg
                AddPathLeg(from.parentSegment, from.splineT, to.splineT, targetList, ref totalLength);
            }
            else
            {
                // Fallback for disjointed stops
                totalLength = Vector3.Distance(from.transform.position, to.transform.position);
            }
            return;
        }

        // 2. Start Segment
        RoadSegment startSeg = from.parentSegment;
        if(startSeg)
        {
            float exitT = (nodes[0] == startSeg.NodeA) ? 0f : 1f;
            AddPathLeg(startSeg, from.splineT, exitT, targetList, ref totalLength);
        }

        // 3. Middle Segments
        for (int i = 0; i < nodes.Count - 1; i++)
        {
            RoadNode nA = nodes[i];
            RoadNode nB = nodes[i + 1];
            
            foreach (var seg in nA.ConnectedRoads)
            {
                if (seg.GetConnectedNode(nA) == nB)
                {
                    float tStart = (seg.NodeA == nA) ? 0f : 1f;
                    float tEnd = (seg.NodeA == nA) ? 1f : 0f;
                    AddPathLeg(seg, tStart, tEnd, targetList, ref totalLength);
                    break;
                }
            }
        }

        // 4. End Segment
        RoadSegment endSeg = to.parentSegment;
        if(endSeg && endSeg != startSeg) 
        {
            float entryT = (nodes.Last() == endSeg.NodeA) ? 0f : 1f;
            AddPathLeg(endSeg, entryT, to.splineT, targetList, ref totalLength);
        }
    }

    [ContextMenu("Debug Server Position")]
public void DebugShowServerPosition()
{
    // This only works on the Server instance
    if (!IsServer && !IsHost) 
    {
        Debug.LogWarning("Cannot debug Server Position from a Client instance.");
        return;
    }

    if (GetCurrentSegmentAndT(out RoadSegment seg, out float t, out bool headingToB))
    {
        if (debugMarkerSpawner != null)
        {
            // Calculate the exact world position based on SERVER data
            Vector3 serverPos = seg.GetPointOnRoad(t, headingToB);
            debugMarkerSpawner.SpawnMarkerAtHitLocation(serverPos);
            Debug.Log($"[Bus Server Debug] Dist: {m_ServerDistanceTraveled:F1}, Seg: {seg.name}, T: {t:F2}");
        }
    }
}
}