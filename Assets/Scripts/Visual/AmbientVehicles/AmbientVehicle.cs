using UnityEngine;

public class AmbientVehicle : MonoBehaviour
{
    [Header("Settings")]
    public float baseSpeed = 20f; 
    public float rotationSpeed = 10f; 
    public string dissolvePropertyName = "_Dissolve"; // Ensure this matches your shader reference
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
        
        // Force fully invisible at the exact moment of spawn to guarantee no pop-in
        ApplyFade(_currentFade);
    }

    // THE FIX: Added 'bool isVisible' parameter for Frustum Culling
    public bool CustomUpdate(float deltaTime, bool isVisible)
    {
        // 1. VISUAL FADING
        // Only spend CPU updating the shader if the car is on-screen OR actively dying
        if ((isVisible || IsDespawning) && !Mathf.Approximately(_currentFade, _targetFade))
        {
            _currentFade = Mathf.MoveTowards(_currentFade, _targetFade, deltaTime * fadeSpeed);
            ApplyFade(_currentFade);
        }

        // 2. DEATH CHECK
        if (IsDespawning)
        {
            return _currentFade >= 1f; 
        }

        // 3. POSITION MATH
        // Always run this, even if off-screen, so the simulation keeps flowing
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

        float currentT = DistanceTraveledOnSegment / CurrentSegment.Length;
        float evalT = IsHeadingToNodeB ? currentT : (1f - currentT);

        Vector3 newPos = CurrentSegment.GetPointOnRoad(evalT, IsHeadingToNodeB);
        transform.position = newPos;

        // 4. ROTATION MATH (Frustum Culled)
        // Skip this expensive Quaternion math entirely if the player isn't looking at the car
        if (isVisible && CurrentSegment.Container != null)
        {
            Vector3 tangent = (Vector3)CurrentSegment.Container.EvaluateTangent(evalT);
            if (!IsHeadingToNodeB) tangent = -tangent;

            Vector3 dir = tangent;
            dir.y = 0; 
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
        if (IsDespawning) return; // Prevent zombie locks

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