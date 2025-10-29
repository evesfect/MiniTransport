# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MiniTransport is a Unity 2023+ project for managing a public transportation company in a small town, with a team of five. The project uses the Universal Render Pipeline (URP) and includes RTS-style camera controls with advanced object selection capabilities.

## Unity Project Configuration

- **Unity Version**: 2023+ (created with URP Blank template 17.0.14)
- **Render Pipeline**: Universal Render Pipeline (URP) 17.2.0
- **Scripting Backend**: IL2CPP for Android, Mono for other platforms
- **Input System**: New Input System (com.unity.inputsystem 1.14.2)
- **Platform Targets**: Windows/Mac/Linux Standalone, Android, iOS

## Core Architecture

### Camera System (Assets/RTSCamera/)

The camera system provides RTS-style controls with terrain-aware positioning:

**RTSCameraController.cs** (`Assets/RTSCamera/CamUtility/RTSCameraController.cs`)
- Orbital camera that rotates around a focus point on terrain
- Mouse scroll wheel for zoom (configurable min/max distances)
- Left mouse button drag for rotation (yaw/pitch)
- Middle mouse button drag for panning
- Space key to reset focus to terrain center
- Smoothed movement with configurable damping
- `blockRotation` flag can be set by external scripts to disable rotation temporarily
- Uses spherical coordinates for camera positioning
- **Multi-terrain support**: Supports multiple terrain objects via `List<Transform> terrains`
- **Efficient terrain detection**: Uses `terrainLayerMask` with raycasting for fast height detection
- **Supports non-Terrain surfaces**: Can use quads, planes, or any mesh as terrain
- **Object tracking system**:
  - `StartTrackingObject(Transform)`: Continuously follow a moving object
  - `StopTracking()`: Stop tracking and return to manual control
  - `FocusOnObject(Transform)`: One-time focus without continuous tracking
  - `IsTracking()`: Check if currently tracking an object
  - `GetTrackedObject()`: Get the currently tracked transform
  - `enableTrackingBreakOnPan`: Automatically stop tracking when user pans manually
  - `trackingSmoothing`: Configurable smoothing for tracked object movement

**SelectionBoxController.cs** (`Assets/RTSCamera/Selection/SelectionController.cs`)
- Two selection modes:
  1. **Normal 3D Selection** (Right-click drag): Draws terrain-following selection box
  2. **2D Screen Selection** (Alt+Right-click drag): Screen-space rectangular selection
- Multi-selection support with Ctrl key
- **F key**: Focus/track selected objects
  - Single object: Can automatically track (if `autoTrackSingleSelection` enabled)
  - Multiple objects: Focus on center point
- **Multi-terrain support**: `List<Transform> terrains` with `terrainLayerMask` for efficient detection
- **Camera integration**:
  - `autoTrackSingleSelection`: Automatically track single selected objects
  - `focusOnSelection`: Auto-focus camera on selection
- Uses QuickOutline for visual feedback on selected objects
- Terrain-aware line rendering with configurable smoothing (Catmull-Rom splines)
- Intelligently filters terrain objects from selection results

**Outline.cs** (`Assets/RTSCamera/QuickOutline/Scripts/Outline.cs`)
- Third-party outline shader system for object highlighting
- Multiple outline modes (OutlineAll, OutlineVisible, OutlineHidden, etc.)
- Configurable outline color and width
- Smooth normals computed via vertex grouping
- Can precompute outline data in editor or at runtime

### Key Interaction Patterns

- **Selection to Camera Focus**: When objects are selected, camera automatically pans to focus on their center point
- **Object Tracking**: Camera can continuously track moving objects (vehicles, units)
  - Single-selected objects can auto-track (optional)
  - F key to manually focus/track selected objects
  - Tracking breaks automatically when user pans camera
- **Efficient Terrain Detection**: Uses raycast with layer masks for fast terrain height queries
  - Primary: Physics.Raycast with `terrainLayerMask` (fast, works with any collider)
  - Fallback: `Terrain.SampleHeight()` for Unity Terrain objects
  - Supports quads, planes, and custom meshes as terrain surfaces
- **Multi-Terrain Support**: Can handle multiple terrain chunks or different ground surfaces simultaneously

## Development Commands

### Opening the Project
Open the project in Unity Hub or directly via Unity Editor. The main scene is likely in `Assets/Scenes/`.

### Testing in Unity
- Use Unity's Play Mode (Ctrl/Cmd+P) to test
- Camera controls:
  - Scroll wheel: Zoom in/out
  - Left mouse drag: Rotate camera
  - Middle mouse drag: Pan camera (breaks tracking)
  - Space: Reset focus to terrain center (stops tracking)
- Selection controls:
  - Right-click drag: 3D terrain-following selection
  - Alt+Right-click drag: 2D screen-space selection
  - F key: Focus/track selected objects
  - Ctrl+click: Multi-select/deselect

### Building
Use Unity's Build Settings (File > Build Settings) to build for target platforms. The project supports:
- Windows/Mac/Linux Standalone
- Android (IL2CPP backend)
- iOS

## Input System

The project uses Unity's new Input System with action maps defined in `Assets/InputSystem_Actions.inputactions`:

- **Player Action Map**: Move, Look, Attack, Interact, Jump, Crouch, Sprint, Previous/Next
- **UI Action Map**: Navigate, Submit, Cancel, Point, Click, RightClick, MiddleClick, ScrollWheel
- **RTS Camera Action Map**: CameraRotate, CameraPan, CameraZoom, MouseDelta, MousePosition, ResetFocus, Select, FocusSelection, ModifierAlt, ModifierCtrl
- Control schemes: Keyboard&Mouse, Gamepad, Touch, Joystick, XR

The RTS camera and selection systems use event-driven input callbacks for better performance:
- Both `RTSCameraController` and `SelectionBoxController` require an `InputActionAsset` reference
- Assign the `InputSystem_Actions` asset to the `Input Actions` field in the Inspector
- Actions are automatically enabled/disabled via `OnEnable()`/`OnDisable()` lifecycle methods
- Input state is tracked via callbacks rather than polling in Update()

## Setting Up RTS Camera System

When setting up RTS camera in a new scene:

1. **RTSCameraController Setup**:
   - Add `RTSCameraController` component to camera GameObject
   - Assign `InputSystem_Actions` asset to `Input Actions` field
   - Add terrain(s) to `Terrains` list
   - Configure `Terrain Layer Mask` to include terrain layers
   - Set camera speeds, zoom limits, and pitch angles as needed

2. **SelectionBoxController Setup**:
   - Create empty GameObject for selection controller
   - Add `SelectionBoxController` component
   - Assign same `InputSystem_Actions` asset to `Input Actions` field
   - Add terrain(s) to `Terrains` list (same as camera controller)
   - Set `Terrain Layer Mask` (same as camera controller)
   - Assign the `RTSCameraController` to `Camera Controller` field
   - Configure `Selectable Layer` to specify which objects can be selected

3. **Input Actions Asset**:
   - The `InputSystem_Actions.inputactions` asset must be in the project
   - It contains the "RTS Camera" action map with all required bindings
   - No need to enable both input systems - new Input System only is sufficient

## Important Packages

- **Unity.InputSystem** (1.14.2): New input system
- **Unity.RenderPipelines.Universal** (17.2.0): URP rendering
- **Unity.AI.Navigation** (2.0.9): NavMesh and pathfinding
- **Unity.2D.Tilemap** (1.0.0) and **Extras** (5.0.1): 2D tilemap support (may be for UI or terrain editing)

## Coding Conventions

### C# Scripts
- MonoBehaviours inherit from `MonoBehaviour` (standard Unity pattern)
- Use `[Header]` attributes to organize Inspector sections
- Use `[HideInInspector]` for public fields that shouldn't be exposed
- Terrain references stored as `List<Transform> terrains` for multi-terrain support
- Physics queries use layer masks (`LayerMask selectableLayer`, `LayerMask terrainLayerMask`)
- Efficient raycasting preferred over direct Terrain API calls
- Input handling uses new Input System with event-driven callbacks (no legacy Input API)

### Terrain Interaction
When working with terrain-based features, use raycast-first approach:
```csharp
// Get terrain height at world position (efficient raycast method)
float GetTerrainHeight(Vector3 worldPos)
{
    Vector3 rayStart = new Vector3(worldPos.x, 1000f, worldPos.z);

    // Primary: Use raycast with terrain layer mask (fast)
    if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 1500f, terrainLayerMask))
    {
        return hit.point.y;
    }

    // Fallback: Check terrain list for Unity Terrain objects
    foreach (Transform terrainTransform in terrains)
    {
        Terrain t = terrainTransform.GetComponent<Terrain>();
        if (t != null && IsPositionInTerrainBounds(worldPos, t))
        {
            return t.SampleHeight(worldPos) + terrainTransform.position.y;
        }
    }

    return worldPos.y;
}
```

### Camera Focus Integration
When implementing new features that interact with the camera:
```csharp
// One-time focus (without tracking)
if (cameraController != null)
    cameraController.SetTargetFocusPoint(newFocusPoint);

// Focus on a specific object once
if (cameraController != null)
    cameraController.FocusOnObject(targetTransform);

// Start continuous tracking of a moving object
if (cameraController != null)
    cameraController.StartTrackingObject(targetTransform);

// Stop tracking
if (cameraController != null)
    cameraController.StopTracking();

// Check if currently tracking
if (cameraController != null && cameraController.IsTracking())
{
    Transform tracked = cameraController.GetTrackedObject();
    // Do something with tracked object
}
```

### Input System Integration
When adding new input functionality to RTS systems:

```csharp
using UnityEngine.InputSystem;

public class MyRTSFeature : MonoBehaviour
{
    [Header("Input")]
    public InputActionAsset inputActions;

    private InputAction myAction;
    private bool myInputState = false;

    void Awake()
    {
        // Initialize actions from RTS Camera action map
        if (inputActions != null)
        {
            var rtsCameraMap = inputActions.FindActionMap("RTSCamera");
            myAction = rtsCameraMap.FindAction("MyAction");
        }
    }

    void OnEnable()
    {
        // Subscribe to callbacks
        if (myAction != null)
        {
            myAction.started += OnMyActionStarted;
            myAction.performed += OnMyActionPerformed;
            myAction.canceled += OnMyActionCanceled;
            myAction.Enable();
        }
    }

    void OnDisable()
    {
        // Unsubscribe from callbacks
        if (myAction != null)
        {
            myAction.started -= OnMyActionStarted;
            myAction.performed -= OnMyActionPerformed;
            myAction.canceled -= OnMyActionCanceled;
            myAction.Disable();
        }
    }

    // Callback methods
    private void OnMyActionStarted(InputAction.CallbackContext context)
    {
        myInputState = true;
    }

    private void OnMyActionPerformed(InputAction.CallbackContext context)
    {
        // Read value for continuous actions
        Vector2 value = context.ReadValue<Vector2>();
    }

    private void OnMyActionCanceled(InputAction.CallbackContext context)
    {
        myInputState = false;
    }

    void Update()
    {
        // Use input state tracked by callbacks
        if (myInputState)
        {
            // Process input
        }
    }
}
```

## Project Structure

```
Assets/
├── RTSCamera/              # RTS camera and selection system
│   ├── CamUtility/         # RTSCameraController
│   ├── Selection/          # SelectionBoxController
│   └── QuickOutline/       # Outline effect for selections
├── Scenes/                 # Unity scenes
├── Scripts/                # Custom game scripts (currently empty)
├── Settings/               # URP and project settings
└── InputSystem_Actions.inputactions  # Input action definitions
```

## Current Development Stage

The project appears to be in early development with:
- ✅ Core RTS camera system with object tracking
- ✅ Selection system with terrain-following and screen-space modes
- ✅ Multi-terrain support with efficient raycasting
- ✅ Object tracking and focus system
- ✅ QuickOutline visual feedback
- ⚠️ No game-specific scripts yet (Scripts/ folder is empty)
- ⚠️ No transportation management logic implemented yet

When adding new features, follow the existing architectural pattern of:
1. **Terrain-aware positioning**: Use raycast with `terrainLayerMask` for efficient height queries
2. **Camera integration**: Use tracking for moving objects, focus for static positions
3. **Multi-terrain support**: Store terrain references in `List<Transform>` not single Transform
4. **Layer masks**: Use layer masks for efficient Physics queries
5. **Clear separation**: Keep camera control separate from game logic
