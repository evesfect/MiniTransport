using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem; // <-- New Input System

public class UIRaycastDebugger : MonoBehaviour
{
    void Update()
    {
        // Safety check to ensure a mouse exists
        if (Mouse.current == null) return;

        // Check for click using the new Input System
        if (Mouse.current.leftButton.wasPressedThisFrame) 
        {
            if (EventSystem.current == null)
            {
                Debug.LogError("<color=red>[UI Debugger]</color> No EventSystem found in the scene!");
                return;
            }

            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                // Get mouse position using the new Input System
                position = Mouse.current.position.ReadValue() 
            };

            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            if (results.Count == 0)
            {
                Debug.Log("<color=orange>[UI Debugger]</color> Clicked, but the UI raycast hit absolutely NOTHING. (Check GraphicRaycaster on Canvas)");
                return;
            }

            Debug.Log($"<color=cyan>[UI Debugger]</color> Clicked and hit {results.Count} UI elements (Top to Bottom):");
            for (int i = 0; i < results.Count; i++)
            {
                Debug.Log($" {i + 1}. Hit: <color=yellow>{results[i].gameObject.name}</color> | Parent: {results[i].gameObject.transform.parent?.name}");
            }
        }
    }
}