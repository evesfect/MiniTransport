using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class RoadPathfinder
{
    /// <summary>
    /// Calculates the optimal path from StartNode to EndNode using A* algorithm.
    /// Returns null if no path is found.
    /// </summary>
    public static List<RoadNode> FindPath(RoadNode startNode, RoadNode endNode)
    {
        if (startNode == null || endNode == null)
        {
            Debug.LogError("Pathfinder: Start or End node is null.");
            return null;
        }

        if (startNode == endNode)
        {
            return new List<RoadNode> { startNode };
        }
        
        // OpenSet: Nodes to be evaluated
        List<RoadNode> openSet = new List<RoadNode> { startNode };
        
        // CameFrom: The path taken to get to a node (Key: Current, Value: Previous)
        Dictionary<RoadNode, RoadNode> cameFrom = new Dictionary<RoadNode, RoadNode>();

        // G-Score: Cost from Start to Node
        Dictionary<RoadNode, float> gScore = new Dictionary<RoadNode, float>();
        gScore[startNode] = 0;

        // F-Score: Estimated total cost (G + Heuristic)
        Dictionary<RoadNode, float> fScore = new Dictionary<RoadNode, float>();
        fScore[startNode] = Heuristic(startNode, endNode);

        while (openSet.Count > 0)
        {
            openSet.Sort((a, b) => GetScore(fScore, a).CompareTo(GetScore(fScore, b))); // for cities too big, should use a priority queue.
            RoadNode current = openSet[0];

            if (current == endNode)
            {
                return ReconstructPath(cameFrom, current);
            }

            openSet.RemoveAt(0);

            foreach (RoadSegment road in current.ConnectedRoads)
            {
                if (road == null) continue;

                RoadNode neighbor = road.GetConnectedNode(current);
                if (neighbor == null) continue;

                // Calculate tentative G-Score
                float tentativeGScore = GetScore(gScore, current) + road.GetCost();

                // If this path is better than any previous one to 'neighbor'
                if (tentativeGScore < GetScore(gScore, neighbor))
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    fScore[neighbor] = gScore[neighbor] + Heuristic(neighbor, endNode);

                    if (!openSet.Contains(neighbor))
                    {
                        openSet.Add(neighbor);
                    }
                }
            }
        }

        Debug.LogWarning($"Pathfinder: Could not find path between {startNode.name} and {endNode.name}");
        return null;
    }

    // helpers

    private static List<RoadNode> ReconstructPath(Dictionary<RoadNode, RoadNode> cameFrom, RoadNode current)
    {
        List<RoadNode> totalPath = new List<RoadNode> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            totalPath.Add(current);
        }
        totalPath.Reverse(); // Path is built backwards, so flip it
        return totalPath;
    }

    // H-Score: Estimated cost remaining (Euclidean Distance / Max Speed)
    // We assume max speed is roughly 100km/h (approx 28m/s) for the heuristic to remain admissible
    private static float Heuristic(RoadNode a, RoadNode b)
    {
        float dist = Vector3.Distance(a.transform.position, b.transform.position);
        // Dividing by a generic max speed so we don't overestimate the cost
        return dist / 28f; 
    }

    private static float GetScore(Dictionary<RoadNode, float> dict, RoadNode node)
    {
        return dict.TryGetValue(node, out float val) ? val : float.MaxValue;
    }
}