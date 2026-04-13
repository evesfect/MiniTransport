using UnityEngine;

public class AmbientVehicle : MonoBehaviour
{
    [Header("Settings")]
    public float baseSpeed = 20f; 
    public float rotationSpeed = 10f; 
    public string dissolvePropertyName = "_Dissolve"; 
    public float fadeSpeed = 3f;
    
    [Header("State (For Manager)")]
    [HideInInspector] public RoadSegment CurrentSegment;
    [HideInInspector] public bool IsHeadingToNodeB;
    [HideInInspector] public float DistanceTraveledOnSegment; 
    [HideInInspector] public bool IsActive = false;
    [HideInInspector] public bool IsDespawning = false;
    
    // --- OPTIMIZATION CACHES ---
    [HideInInspector] public float CachedTrafficLimit = 1.0f;
    [HideInInspector] public bool IsCurrentlyVisible = false;
    [HideInInspector] public float VisCheckTimer = 0f;

    private Renderer[] _renderers;
    private MaterialPropertyBlock _propBlock;
    private int _dissolvePropertyID;
    
    private int _busIntersectCount = 0;
    private float _currentFade = 0f;
    private float _targetFade = 0f;
    private Vector3 _lastPosition; // Used for ultra-cheap rotation math

    public void Initialize()
    {
        _renderers = GetComponentsInChildren<Renderer>();
        _propBlock = new MaterialPropertyBlock();
        _dissolvePropertyID = Shader.PropertyToID(dissolvePropertyName);
        
        _currentFade = 0f;
        _targetFade = 0f;
        ApplyFade(_currentFade);
    }

    public void SpawnReset()
    {
        _busIntersectCount = 0;
        _targetFade = 0f;
        _currentFade = 1f; 
        DistanceTraveledOnSegment = 0f;
        IsDespawning = false;
        IsActive = true;
        
        // Stagger the initial visibility check so 1000 cars don't all check on the exact same frame
        VisCheckTimer = Random.Range(0f, 0.2f);
        IsCurrentlyVisible = false; 

        ApplyFade(_currentFade);
    }

    public bool CustomUpdate(float deltaTime)
    {
        // 1. VISUAL FADING
        if ((IsCurrentlyVisible || IsDespawning) && !Mathf.Approximately(_currentFade, _targetFade))
        {
            _currentFade = Mathf.MoveTowards(_currentFade, _targetFade, deltaTime * fadeSpeed);
            ApplyFade(_currentFade);
        }

        if (IsDespawning) return _currentFade >= 1f; 

        if (CurrentSegment == null || CurrentSegment.Length <= 0) return true;

        // 2. POSITION MATH (Ultra-Fast: No Grid Lookups)
        float step = baseSpeed * CachedTrafficLimit * deltaTime; 
        DistanceTraveledOnSegment += step;

        if (DistanceTraveledOnSegment >= CurrentSegment.Length) return true; 

        float currentT = DistanceTraveledOnSegment / CurrentSegment.Length;
        float evalT = IsHeadingToNodeB ? currentT : (1f - currentT);

        Vector3 newPos = CurrentSegment.GetPointOnRoad(evalT, IsHeadingToNodeB);

        // Initialize _lastPosition on the first frame to prevent snapping
        if (_lastPosition == Vector3.zero) _lastPosition = newPos;

        // 3. ROTATION MATH (Ultra-Fast: Delta Position instead of Spline Tangents)
        if (IsCurrentlyVisible)
        {
            Vector3 dir = newPos - _lastPosition;
            dir.y = 0; 
            
            if (dir.sqrMagnitude > 0.0001f)
            {
                dir.Normalize();
                Quaternion targetRot = Quaternion.LookRotation(dir);
                float timeMult = SimulationTimeManager.Instance != null ? SimulationTimeManager.Instance.TimeMultiplier : 1f;
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, deltaTime * rotationSpeed * timeMult);
            }
        }

        // Apply position and save for next frame's rotation calculation
        transform.position = newPos;
        _lastPosition = newPos;

        return false;
    }

    public void TriggerDespawnFade()
    {
        if (IsDespawning) return; 
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
        if (IsDespawning) return; 

        if (other.CompareTag("Bus") || other.transform.root.CompareTag("Bus")) 
        {
            _busIntersectCount++;
            _targetFade = 0.5f;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsDespawning) return; 

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
}