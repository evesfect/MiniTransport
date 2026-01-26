using System.Collections;
using UnityEngine;

public class MarkerSpawner : MonoBehaviour
{
    public GameObject prefab;
    public float raycastHeight = 400f;
    public LayerMask layerMask;
    public float rayOriginScatter = 100f;
    public float dropDuration = 0.5f;
    public AnimationCurve dropSpeedCurve = AnimationCurve.EaseInOut(0,0,1,1);
    public float minPenetrationOffset = 25f; 
    public float maxPenetratitonOffset = 45f;

    public GameObject testTransformHolder;

    public void SpawnMarkerAtHitLocation(Vector3 targetPos)
    {
        if (prefab == null)
        {
            Debug.LogWarning("Marker prefab to spawn is null");
            return;
        }

        // first cast to find rayTarget
        Vector3 rayOrigin = new Vector3(targetPos.x, raycastHeight, targetPos.z);

        RaycastHit hit;

        if (!Physics.Raycast(rayOrigin, Vector3.down, out hit, Mathf.Infinity, layerMask))
        {
            Debug.LogWarning("MarkerSpawner: Initial raycast did not hit any target");
            return;
        }
        
        Vector3 rayTarget = hit.point;
        rayOrigin = GetRandomPointInSphere(rayOrigin, rayOriginScatter);
        Vector3 flightDirection = (rayTarget - rayOrigin).normalized;
        float penetrationOffset = Random.Range(minPenetrationOffset, maxPenetratitonOffset);
        rayTarget = rayTarget - (flightDirection * penetrationOffset);


        GameObject newMarker = Instantiate(prefab, rayOrigin, Quaternion.identity);
        newMarker.transform.LookAt(rayTarget);
        StartCoroutine(DropMarkerRoutine(newMarker.transform, rayOrigin, rayTarget));
    }

    // Transform input overload
    public void SpawnMarkerAtHitLocation(Transform target)
    {
        if (target == null)
        {
            Debug.LogWarning("Target transform is null");
            return;
        }
        SpawnMarkerAtHitLocation(target.position);
    }

    private IEnumerator DropMarkerRoutine(Transform marker, Vector3 start, Vector3 end)
    {
        float elapsed = 0f;

        while (elapsed < dropDuration)
        {
            if (marker == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / dropDuration;
            float curveValue = dropSpeedCurve.Evaluate(t);
            marker.position = Vector3.Lerp(start, end, curveValue);
            yield return null;
        }
        if (marker != null)
        {
            marker.position = end;
        }
    }

    private Vector3 GetRandomPointInSphere(Vector3 origin, float factor)
    {
        Vector3 randomOffset = Random.insideUnitSphere * factor;
        return origin + randomOffset;
    }

    [ContextMenu("Test spawn")]
    public void TestSpawn()
    {
        SpawnMarkerAtHitLocation(testTransformHolder.transform);
    }
}