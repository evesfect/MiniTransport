using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Tool that raycasts the mouse to terrain, finds the closest bus stop,
/// outlines it, and allows the user to click to add it to the current route.
/// Activated/deactivated by the add-stop toggle in RouteEditPanel.
/// </summary>
public class BusStopAddTool : MonoBehaviour
{
    [Header("Settings")]
    public LayerMask terrainLayerMask = -1;
    public float maxSearchRadius = 50f;

    [Header("Input")]
    public InputActionAsset inputActions;

    [Header("Outline")]
    public Color outlineColor = Color.green;
    public Color removeOutlineColor = Color.red;
    public float outlineWidth = 4f;

    private RouteEditPanel _editPanel;
    private bool _isActive;
    private BusStop _hoveredStop;
    private Outline _currentOutline;
    private Camera _mainCamera;

    private InputAction _mousePositionAction;

    private void Awake()
    {
        if (inputActions != null)
        {
            var cameraMap = inputActions.FindActionMap("Camera");
            if (cameraMap != null)
            {
                _mousePositionAction = cameraMap.FindAction("MousePosition");
            }
        }
    }

    public void Activate(RouteEditPanel editPanel)
    {
        _editPanel = editPanel;
        _isActive = true;
        _mainCamera = Camera.main;

        if (_mousePositionAction != null) _mousePositionAction.Enable();

        var sel = Object.FindAnyObjectByType<SelectionBoxController>();
        if (sel != null) sel.blockSelection = true;
    }

    public void Deactivate()
    {
        _isActive = false;
        ClearOutline();
        _hoveredStop = null;
        _editPanel = null;

        var sel = Object.FindAnyObjectByType<SelectionBoxController>();
        if (sel != null) sel.blockSelection = false;
    }

    private void Update()
    {
        if (!_isActive || _editPanel == null) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        // Don't raycast if pointer is over UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            ClearOutline();
            return;
        }

        Vector2 mousePos = _mousePositionAction != null
            ? _mousePositionAction.ReadValue<Vector2>()
            : Vector2.zero;

        Ray ray = _mainCamera.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, terrainLayerMask))
        {
            BusStop closest = FindClosestBusStop(hit.point);

            if (closest != _hoveredStop)
            {
                ClearOutline();
                _hoveredStop = closest;

                if (_hoveredStop != null)
                    ApplyOutline(_hoveredStop);
            }
        }
        else
        {
            ClearOutline();
            _hoveredStop = null;
        }

        if (_hoveredStop == null) return;

        // Left click to add
        if (mouse.leftButton.wasPressedThisFrame)
        {
            _editPanel.AddStop(_hoveredStop.stopID);
        }

        // Right click to remove
        if (mouse.rightButton.wasPressedThisFrame)
        {
            _editPanel.RemoveStop(_hoveredStop.stopID);
        }
    }

    private BusStop FindClosestBusStop(Vector3 worldPos)
    {
        if (TransportManager.Instance == null) return null;

        BusStop closest = null;
        float closestDist = maxSearchRadius;

        // Use the stop registry from TransportManager (search all registered stops)
        var allStops = Object.FindObjectsByType<BusStop>(FindObjectsSortMode.None);
        foreach (var stop in allStops)
        {
            float dist = Vector3.Distance(worldPos, stop.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = stop;
            }
        }

        return closest;
    }

    private void ApplyOutline(BusStop stop)
    {
        _currentOutline = stop.GetComponent<Outline>();
        if (_currentOutline == null)
            _currentOutline = stop.gameObject.AddComponent<Outline>();

        bool isInRoute = _editPanel != null && _editPanel.CurrentRoute != null
            && _editPanel.CurrentRoute.StopIDs.Contains(stop.stopID);

        _currentOutline.OutlineMode = Outline.Mode.OutlineAll;
        _currentOutline.OutlineColor = isInRoute ? removeOutlineColor : outlineColor;
        _currentOutline.OutlineWidth = outlineWidth;
        _currentOutline.enabled = true;
    }

    private void ClearOutline()
    {
        if (_currentOutline != null)
        {
            _currentOutline.enabled = false;
            _currentOutline = null;
        }
        _hoveredStop = null;
    }
}
