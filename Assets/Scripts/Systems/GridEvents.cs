using System;
using UnityEngine;

public static class GridEvents
{
    public static event Action<int, int> OnPopulationChanged; // tileIndex, newPopulation

    public static void TriggerPopulationChanged(int tileIndex, int newPopulation) 
    {
        OnPopulationChanged?.Invoke(tileIndex, newPopulation);
    }
}