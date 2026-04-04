using UnityEngine;

public class AmbientVehicle : MonoBehaviour
{
    [Header("Settings")]
    public float baseSpeed = 20f; // Match your Bus baseSpeed
    public float rotationSpeed = 10f; // Match VehicleDriver rotation speed
    public string dissolvePropertyName = "_Dissolve";
    public float fadeSpeed = 3f;
    
    [Header("State (For Manager)")]
    [HideInInspector] public RoadSegment CurrentSegment;
    [HideInInspector] public bool IsHeadingToNodeB;
    [HideInInspector] public float DistanceTraveledOnSegment; 
    [HideInInspector] public bool IsActive = false;
    [HideInInspector] public bool IsDespawning = false;

    private Renderer[] _renderers;
    private MaterialPropertyBlock _propBlock;
    private int _dissolvePropertyID;
    
    private int _busIntersectCount = 0;
    private float _currentFade = 0f;
    private float _targetFade = 0f;

    public void Initialize()
    {
        _renderers = GetComponentsInChildren<Renderer>();
        _propBlock = new MaterialPropertyBlock();
        _dissolvePropertyID = Shader.PropertyToID(dissolvePropertyName);
        
        // Force fully opaque at start
        _currentFade = 0f;
        _targetFade = 0f;
        ApplyFade(_currentFade);
    }

    public void SpawnReset()
    {
        _busIntersectCount = 0;
        _targetFade = 0f;
        _currentFade = 1f; // Start fully dissolved so it fades IN
        DistanceTraveledOnSegment = 0f;
        IsDespawning = false;
        ApplyFade(_currentFade);
    }

    // Returns TRUE if the vehicle needs Manager attention (either reached end of road OR finished despawning)
    public bool CustomUpdate(float deltaTime)
    {
        // 1. HANDLE VISUAL FADING
        if (!Mathf.Approximately(_currentFade, _targetFade))
        {
            _currentFade = Mathf.MoveTowards(_currentFade, _targetFade, deltaTime * fadeSpeed);
            ApplyFade(_currentFade);
        }

        // 2. DESPAWN STATE CHECK
        if (IsDespawning)
        {
            // If we are despawning, stop moving. Tell manager to pool us ONLY when fade is 100% complete.
            return _currentFade >= 1f; 
        }

        // 3. MOVEMENT & ROTATION LOGIC (MATCHES VehicleDriver.cs EXACTLY)
        if (CurrentSegment == null || CurrentSegment.Length <= 0) return true;

        float localTraffic = 1.0f;
        if (GridManager.Instance != null)
        {
            localTraffic = GridManager.Instance.GetTrafficModifierAt(transform.position);
        }

        float step = baseSpeed * localTraffic * deltaTime; 
        DistanceTraveledOnSegment += step;

        if (DistanceTraveledOnSegment >= CurrentSegment.Length)
        {
            return true; // Reached end of segment
        }

        // Calculate T on Spline
        float currentT = DistanceTraveledOnSegment / CurrentSegment.Length;
        float evalT = IsHeadingToNodeB ? currentT : (1f - currentT);

        // Position
        Vector3 newPos = CurrentSegment.GetPointOnRoad(evalT, IsHeadingToNodeB);
        transform.position = newPos;

        // Rotation via Spline Tangent
        if (CurrentSegment.Container != null)
        {
            Vector3 tangent = (Vector3)CurrentSegment.Container.EvaluateTangent(evalT);
            
            // Invert tangent if moving backwards (from VehicleDriver)
            if (!IsHeadingToNodeB) tangent = -tangent;

            Vector3 dir = tangent;
            dir.y = 0; // Flatness constraint
            dir.Normalize();

            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                float timeMult = SimulationTimeManager.Instance != null ? SimulationTimeManager.Instance.TimeMultiplier : 1f;
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, deltaTime * rotationSpeed * timeMult);
            }
        }

        return false;
    }

    public void TriggerDespawnFade()
    {
        if (IsDespawning) return; // Prevent spamming
        _targetFade = 1f;
        IsDespawning = true;
    }

    private void ApplyFade(float value)
    {
        foreach (var rend in _renderers)
        {
            rend.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(_dissolvePropertyID, value);
            rend.SetPropertyBlock(_propBlock);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Check the object itself, or the root parent object
        if (other.CompareTag("Bus") || other.transform.root.CompareTag("Bus")) 
        {
            _busIntersectCount++;
            _targetFade = 0.5f;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Bus") || other.transform.root.CompareTag("Bus"))
        {
            _busIntersectCount--;
            if (_busIntersectCount <= 0)
            {
                _busIntersectCount = 0;
                if (!IsDespawning) _targetFade = 0f;
            }
        }
    }

    private void OnDrawGizmos()
    {
        // Draw the trigger sphere/box
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f); // Semi-transparent green
            Gizmos.matrix = transform.localToWorldMatrix;
            
            if (col is SphereCollider sphere)
            {
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
            else if (col is BoxCollider box)
            {
                Gizmos.DrawWireCube(box.center, box.size);
            }
        }
    }
}