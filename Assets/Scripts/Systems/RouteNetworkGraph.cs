using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Server-side reachability model of the bus route network used to plan passenger
/// transfers. Computes the minimum number of buses ("rides") needed to travel between two
/// grid tiles, where transfers = rides - 1.
///
/// Boarding is decided with a strict-progress rule (see <see cref="TryPlanLeg"/>): a
/// passenger only boards a bus when some upcoming stop on that bus brings them strictly
/// closer (in rides) to their destination. Because every accepted ride decreases a
/// non-negative integer, the journey always terminates - no dead ends, no infinite hopping.
/// </summary>
public class RouteNetworkGraph : MonoBehaviour
{
    public static RouteNetworkGraph Instance { get; private set; }

    [Header("Policy")]
    [Tooltip("Maximum transfers a passenger will accept (rides = transfers + 1).")]
    [SerializeField] private int maxTransfers = 2;

    private int MaxRides => maxTransfers + 1;

    // routeID -> set of grid tiles the route serves
    private readonly Dictionary<string, HashSet<int>> _routeTiles = new Dictionary<string, HashSet<int>>();
    // grid tile -> routes serving it (transfer candidates)
    private readonly Dictionary<int, List<string>> _tileRoutes = new Dictionary<int, List<string>>();
    // memoised MinRides results, cleared on every rebuild
    private readonly Dictionary<long, int> _minRidesCache = new Dictionary<long, int>();

    private bool _dirty = true;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        if (TransportManager.Instance != null)
        {
            TransportManager.Instance.OnRoutesChanged -= MarkDirty;
            TransportManager.Instance.OnRoutesChanged += MarkDirty;
        }
    }

    private void Start()
    {
        // TransportManager may not have existed yet during OnEnable.
        if (TransportManager.Instance != null)
        {
            TransportManager.Instance.OnRoutesChanged -= MarkDirty;
            TransportManager.Instance.OnRoutesChanged += MarkDirty;
        }
    }

    private void OnDisable()
    {
        if (TransportManager.Instance != null)
            TransportManager.Instance.OnRoutesChanged -= MarkDirty;
    }

    public void MarkDirty() => _dirty = true;

    private void EnsureBuilt()
    {
        if (_dirty) Rebuild();
    }

    public void Rebuild()
    {
        _routeTiles.Clear();
        _tileRoutes.Clear();
        _minRidesCache.Clear();

        if (TransportManager.Instance == null || GridManager.Instance == null)
        {
            // Managers not ready yet; stay dirty so we retry on next use.
            _dirty = true;
            return;
        }

        _dirty = false;

        foreach (var route in TransportManager.Instance.ActiveRoutes)
        {
            if (route == null || route.StopIDs == null) continue;

            var tiles = new HashSet<int>();
            foreach (var stopID in route.StopIDs)
            {
                BusStop stop = TransportManager.Instance.GetStop(stopID);
                if (stop == null) continue;

                if (GridManager.Instance.WorldToGrid(stop.transform.position, out int x, out int y))
                {
                    int tile = GridManager.Instance.GetIndex(x, y);
                    tiles.Add(tile);

                    if (!_tileRoutes.TryGetValue(tile, out var list))
                    {
                        list = new List<string>();
                        _tileRoutes[tile] = list;
                    }
                    if (!list.Contains(route.RouteID)) list.Add(route.RouteID);
                }
            }
            _routeTiles[route.RouteID] = tiles;
        }
    }

    /// <summary>
    /// Minimum number of buses ("rides") needed to travel from <paramref name="origin"/> to
    /// <paramref name="dest"/> over the route network, capped at the transfer policy.
    /// Returns 0 if origin == dest, or <see cref="int.MaxValue"/> if unreachable within the cap.
    /// </summary>
    public int MinRides(int origin, int dest)
    {
        EnsureBuilt();
        if (origin == dest) return 0;

        long key = ((long)origin << 32) ^ (uint)dest;
        if (_minRidesCache.TryGetValue(key, out int cached)) return cached;

        int result = int.MaxValue;

        if (_tileRoutes.TryGetValue(origin, out var originRoutes) && originRoutes.Count > 0)
        {
            // BFS over routes. Every transition adds exactly one ride, so the queue stays in
            // non-decreasing ride order and the first hit is the minimum.
            var visitedRoutes = new HashSet<string>();
            var frontier = new Queue<(string route, int rides)>();

            foreach (var r in originRoutes)
            {
                if (visitedRoutes.Add(r)) frontier.Enqueue((r, 1));
            }

            while (frontier.Count > 0)
            {
                var (route, rides) = frontier.Dequeue();
                if (!_routeTiles.TryGetValue(route, out var tiles)) continue;

                if (tiles.Contains(dest)) { result = rides; break; }

                if (rides < MaxRides)
                {
                    foreach (int tile in tiles)
                    {
                        if (!_tileRoutes.TryGetValue(tile, out var connecting)) continue;
                        foreach (var r2 in connecting)
                        {
                            if (visitedRoutes.Add(r2)) frontier.Enqueue((r2, rides + 1));
                        }
                    }
                }
            }
        }

        _minRidesCache[key] = result;
        return result;
    }

    /// <summary>
    /// Decides whether a passenger heading to <paramref name="dest"/> should board a bus
    /// whose upcoming stops (in arrival order) are <paramref name="upcomingTilesInOrder"/>.
    /// Boards only if some upcoming stop strictly reduces the remaining rides; the chosen
    /// <paramref name="alightTile"/> is the earliest upcoming stop achieving the best progress.
    /// </summary>
    public bool TryPlanLeg(int currentTile, List<int> upcomingTilesInOrder, int dest, out int alightTile)
    {
        alightTile = -1;
        EnsureBuilt();

        if (upcomingTilesInOrder == null || upcomingTilesInOrder.Count == 0) return false;

        int baseline = MinRides(currentTile, dest);
        if (baseline == int.MaxValue) return false; // unreachable within transfer policy

        int bestRides = baseline;
        foreach (int u in upcomingTilesInOrder)
        {
            int r = (u == dest) ? 0 : MinRides(u, dest);
            if (r < bestRides)
            {
                bestRides = r;
                alightTile = u;
                if (r == 0) break; // arriving directly - cannot do better
            }
        }

        return alightTile != -1;
    }
}
