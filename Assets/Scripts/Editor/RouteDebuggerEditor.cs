using UnityEngine;
using UnityEditor;
using System.Linq;

[CustomEditor(typeof(RouteDebugger))]
public class RouteDebuggerEditor : Editor
{
    private RouteDebugger debugger;
    private int selectedIndex = 0; // Index of the currently selected route in the dropdown

    private void OnEnable()
    {
        debugger = (RouteDebugger)target;
        UpdateSelectedIndex();
    }

    private void UpdateSelectedIndex()
    {
        // Find the index corresponding to the current targetRouteName
        selectedIndex = debugger.AvailableRouteNames
            .IndexOf(debugger.targetRouteName);

        if (selectedIndex < 0)
        {
            // If the name is not found (e.g., a new name is typed, or list is empty)
            selectedIndex = 0; 
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Find the properties we need to draw manually
        SerializedProperty targetRouteNameProp = serializedObject.FindProperty("targetRouteName");
        SerializedProperty routeColorProp = serializedObject.FindProperty("routeColor");

        // ROUTE SELECTION
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Route Manager", EditorStyles.boldLabel);

        var routeNames = debugger.AvailableRouteNames;
        
        // Ensure the selectedIndex is valid (especially after domain reload or list change)
        if (selectedIndex >= routeNames.Count || selectedIndex < 0)
        {
            UpdateSelectedIndex();
        }

        // Draw the Dropdown/Popup
        // The popup returns the index of the selected item.
        int newIndex = EditorGUILayout.Popup(
            new GUIContent("Target Route Name", "Select an existing route to Load/Edit/Delete, or type a new name below."),
            selectedIndex,
            routeNames.ToArray()
        );

        if (newIndex != selectedIndex)
        {
            selectedIndex = newIndex;
            // Update the underlying string variable based on the selection
            targetRouteNameProp.stringValue = routeNames[selectedIndex];
        }

        // Allow creating a NEW route by typing a name
        EditorGUILayout.PropertyField(targetRouteNameProp, new GUIContent("New? Route Name"));
        EditorGUILayout.PropertyField(routeColorProp);
        

        EditorGUILayout.Space(10);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("editorStops"));
        
        // VISUALIZATION SETTINGS
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Visualization Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("baseHeight"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("heightStep"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lineWidth"));
        
        // CONTEXT MENU BUTTONS
        EditorGUILayout.Space(10);
        
        if (GUILayout.Button("1. Create / Update Route"))
        {
            debugger.CreateOrUpdateRoute();
        }
        if (GUILayout.Button("2. Load Route to Editor"))
        {
            debugger.LoadRouteToEditor();
        }
        if (GUILayout.Button("3. Delete Route"))
        {
            debugger.DeleteRoute();
        }
        if (GUILayout.Button("4. Visualize All Routes"))
        {
            debugger.VisualizeAllRoutes();
        }

        serializedObject.ApplyModifiedProperties();
    }
}