using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class RTSCameraController : MonoBehaviour
{
    #region Properties

    [Header("Camera Settings")]
    public float panSpeed = 200f;
    public float rotationSpeed = 175f;
    public float zoomSpeed = 150f;

    [Header("Zoom Limits")]
    public float minZoomDistance = 10f;
    public float maxZoomDistance = 100f;

    [Header("Pitch Settings")]
    public float pitchAngle = 45f;
    public float minPitchAngle = 20f;
    public float maxPitchAngle = 80f;

    [Header("Focus Settings")]
    public List<Transform> terrains = new List<Transform>();
    public LayerMask terrainLayerMask = -1; // All layers by default
    public bool renderFocusPoint = true;

    [Header("Smoothing Settings")]
    public float rotationDamping = 0.9f;
    public float zoomDamping = 0.9f;
    public float focusSmoothing = 5f;

    [Header("Object Tracking")]
    public bool enableTrackingBreakOnPan = true;
    public float trackingSmoothing = 8f;

    [Header("Input Settings")]
    public InputActionAsset inputActions;

    // This flag disables rotation (set from external scripts)
    [HideInInspector]
    public bool blockRotation = false;

    private float zoomVelocity;
    private float yawVelocity;
    private float pitchVelocity;
    private Vector3 targetFocusPoint;

    private Vector3 focusPoint;
    private float currentZoomDistance = 50f;
    private float currentYaw;

    // Object tracking variables
    [SerializeField]
    private Transform trackedObject;
    private bool isTracking = false;

    // Input Actions
    private InputAction zoomAction;
    private InputAction rotateAction;
    private InputAction panAction;
    private InputAction mouseDeltaAction;
    private InputAction resetFocusAction;

    #endregion

    #region Enable/Disable
    void OnEnable()
    {
        // Get the Camera action map
        var cameraActionMap = inputActions.FindActionMap("Camera");

        // Get individual actions
        zoomAction = cameraActionMap.FindAction("Zoom");
        rotateAction = cameraActionMap.FindAction("Rotate");
        panAction = cameraActionMap.FindAction("Pan");
        mouseDeltaAction = cameraActionMap.FindAction("MouseDelta");
        resetFocusAction = cameraActionMap.FindAction("ResetFocus");

        // Subscribe to reset focus event
        resetFocusAction.performed += OnResetFocus;

        // Enable the action map
        cameraActionMap.Enable();
    }

    void OnDisable()
    {
        // Unsubscribe from events
        resetFocusAction.performed -= OnResetFocus;

        // Disable the action map
        var cameraActionMap = inputActions.FindActionMap("Camera");
        cameraActionMap.Disable();
    }
    #endregion
    void Start()
    {
        focusPoint = GetTerrainCenter();
        targetFocusPoint = focusPoint;
        Vector3 offset = transform.position - focusPoint;
        offset.y = 0;
        currentYaw = (offset.sqrMagnitude > 0.001f) ? Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg : 0;
    }

    void Update()
    {
        ProcessZoom();
        ProcessRotation();
        ProcessPan();
        UpdateFocus();
        UpdateCameraPosition();
    }

    private void ProcessZoom()
    {
        float scrollInput = zoomAction.ReadValue<float>();
        if (Mathf.Abs(scrollInput) > 0.01f)
            zoomVelocity += scrollInput * zoomSpeed;
        else
            zoomVelocity *= zoomDamping;

        currentZoomDistance = Mathf.Clamp(
            currentZoomDistance - zoomVelocity * Time.deltaTime,
            minZoomDistance,
            maxZoomDistance
        );
    }

    private void ProcessRotation()
    {
        // Disable rotation if blockRotation is true.
        if (blockRotation)
        {
            yawVelocity = 0;
            pitchVelocity = 0;
            return;
        }

        if (rotateAction.IsPressed())
        {
            // Rotate camera using mouse movement.
            Vector2 mouseDelta = mouseDeltaAction.ReadValue<Vector2>();
            yawVelocity = mouseDelta.x * rotationSpeed * 0.01f; // Adjust multiplier as needed
            pitchVelocity = mouseDelta.y * rotationSpeed * 0.01f;
        }
        else
        {
            yawVelocity *= rotationDamping;
            pitchVelocity *= rotationDamping;
        }

        currentYaw += yawVelocity * Time.deltaTime;
        pitchAngle = Mathf.Clamp(pitchAngle + pitchVelocity * Time.deltaTime, minPitchAngle, maxPitchAngle);
    }

    private void ProcessPan()
    {
        if (panAction.IsPressed())
        {
            // Break tracking when user manually pans
            if (isTracking && enableTrackingBreakOnPan)
                StopTracking();

            Vector2 mouseDelta = mouseDeltaAction.ReadValue<Vector2>();
            float deltaX = -mouseDelta.x * 0.01f; // Adjust multiplier as needed
            float deltaY = mouseDelta.y * 0.01f;
            Vector3 right = transform.right;
            Vector3 forward = Vector3.Cross(Vector3.up, right);
            Vector3 inputDirection = right * deltaX + forward * deltaY;
            targetFocusPoint += inputDirection * panSpeed * Time.deltaTime;
        }
        // Keep the focus on terrain during manual pan.
        targetFocusPoint.y = GetTerrainHeight(targetFocusPoint);
    }

    private void UpdateFocus()
        {
            // Update target focus point if tracking an object
            if (IsTracking())
            {
                Vector3 trackedPos = trackedObject.position;
                //trackedPos.y = GetTerrainHeight(trackedPos);
                targetFocusPoint = Vector3.Lerp(targetFocusPoint, trackedPos, trackingSmoothing * Time.deltaTime);
            }

            focusPoint = Vector3.Lerp(focusPoint, targetFocusPoint, focusSmoothing * Time.deltaTime);
        }

    private void UpdateCameraPosition()
    {
        float yawRad = currentYaw * Mathf.Deg2Rad;
        float pitchRad = pitchAngle * Mathf.Deg2Rad;
        Vector3 offsetPos = new Vector3(
            currentZoomDistance * Mathf.Sin(pitchRad) * Mathf.Sin(yawRad),
            currentZoomDistance * Mathf.Cos(pitchRad),
            currentZoomDistance * Mathf.Sin(pitchRad) * Mathf.Cos(yawRad)
        );
        transform.position = focusPoint + offsetPos;
        transform.LookAt(focusPoint);
    }

    #region Focus

    private void OnResetFocus(InputAction.CallbackContext context)
    {
        StopTracking();
        targetFocusPoint = GetTerrainCenter();
    }

    // Called externally to update the camera's focus.
    public void SetTargetFocusPoint(Vector3 newTargetFocusPoint)
    {
        targetFocusPoint = newTargetFocusPoint;
        // Stop tracking when focus is set manually
        if (isTracking)
            StopTracking();
    }

    // Focus on an object without continuous tracking
    public void FocusOnObject(Transform target)
    {
        if (target != null)
        {
            Vector3 targetPos = target.position;
            //targetPos.y = GetTerrainHeight(targetPos);
            SetTargetFocusPoint(targetPos);
        }
    }
    #endregion

    #region Tracking

    // Start tracking a specific object
    public void StartTrackingObject(Transform target)
    {
        if (target != null)
        {
            trackedObject = target;
            isTracking = true;
        }
    }

    // Stop tracking and return to manual camera control
    public void StopTracking()
    {
        isTracking = false;
        trackedObject = null;
    }

    // Check if currently tracking an object
    public bool IsTracking()
    {
        return isTracking && trackedObject != null;
    }

    // Get the currently tracked object
    public Transform GetTrackedObject()
    {
        return trackedObject;
    }

    #endregion

    #region TerrainUtils

    // Returns the terrain height at a given position using efficient raycast to find highest surface
    private float GetTerrainHeight(Vector3 position)
    {
        // Use efficient raycast to find highest surface
        Vector3 rayStart = new Vector3(position.x, 1000f, position.z);
        Vector3 rayDirection = Vector3.down;

        // Cast ray downward to find highest surface
        if (Physics.Raycast(rayStart, rayDirection, out RaycastHit hit, 1500f, terrainLayerMask))
        {
            return hit.point.y;
        }

        // Fallback to terrain list method if raycast misses
        return GetTerrainHeightFallback(position);
    }
    
    private float GetTerrainHeightFallback(Vector3 position)
    {
        foreach (Transform terrainTransform in terrains)
        {
            if (terrainTransform != null)
            {
                Terrain t = terrainTransform.GetComponent<Terrain>();
                if (t != null)
                {
                    Vector3 terrainPos = terrainTransform.position;
                    Vector3 terrainSize = t.terrainData.size;
                    if (position.x >= terrainPos.x && position.x <= terrainPos.x + terrainSize.x &&
                        position.z >= terrainPos.z && position.z <= terrainPos.z + terrainSize.z)
                    {
                        return t.SampleHeight(position) + terrainTransform.position.y;
                    }
                }
                else
                {
                    // Handle quads or other objects - check if position is within bounds
                    Renderer renderer = terrainTransform.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        Bounds bounds = renderer.bounds;
                        if (bounds.Contains(new Vector3(position.x, bounds.center.y, position.z)))
                        {
                            return terrainTransform.position.y;
                        }
                    }
                }
            }
        }
        return focusPoint.y;
    }

    // Returns the center point of the first terrain in the list.
    private Vector3 GetTerrainCenter()
    {
        if (terrains.Count > 0 && terrains[0] != null)
        {
            Terrain t = terrains[0].GetComponent<Terrain>();
            if (t != null)
                return terrains[0].position + t.terrainData.size / 2f;

            // For quads or other objects, use renderer bounds center
            Renderer renderer = terrains[0].GetComponent<Renderer>();
            if (renderer != null)
                return renderer.bounds.center;

            return terrains[0].position;
        }
        return Vector3.zero;
    }
    #endregion
    
}