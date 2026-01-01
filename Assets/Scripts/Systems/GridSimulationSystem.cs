using UnityEngine;
using Unity.Netcode;

// Base class for all simulation logic scripts
public abstract class GridSimulationSystem : NetworkBehaviour
{
    protected GridManager _grid;

    // Called by the SimulationManager when it starts
    public virtual void Initialize(GridManager grid)
    {
        _grid = grid;
    }
    public abstract void OnSimulationTick(float deltaTime);
}