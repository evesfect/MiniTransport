using UnityEngine;
using System.Collections;

public class SimpleBusSpawner : MonoBehaviour
{
    public GameObject busPrefab;
    public int numberOfBuses = 1;
    public float spawnInterval = 3.0f;

    void Start()
    {
        StartCoroutine(SpawnRoutine());
    }

    IEnumerator SpawnRoutine()
    {
        // Wait one frame to ensure TransportManager has loaded routes
        yield return null; 

        if (TransportManager.Instance.ActiveRoutes.Count == 0)
        {
            Debug.LogError("No routes found! Please create and save a route using RouteDebugger.");
            yield break;
        }

        // Pick the first route for testing
        Route testRoute = TransportManager.Instance.ActiveRoutes[0];
        Debug.Log($"Spawning {numberOfBuses} buses on Route: {testRoute.RouteName}");

        for (int i = 0; i < numberOfBuses; i++)
        {
            GameObject busObj = Instantiate(busPrefab);
            busObj.name = $"TestBus_{i}";
            
            BusController controller = busObj.GetComponent<BusController>();
            
            // Assign the route ID - the bus will auto-start
            controller.AssignRoute(testRoute);

            yield return new WaitForSeconds(spawnInterval);
        }
    }
}