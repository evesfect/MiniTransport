using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class RouteManagerUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject routeListPanel;
    public GameObject routeEditorPanel;
    
    public Transform routeListContent;
    public GameObject routeListItemPrefab; // Button with Text

    public InputField routeNameInput;
    public Text stopCountText;
    public Button createRouteBtn;
    public Button closeEditorBtn;
    
    [Header("Visualization")]
    public RouteVisualizer visualizer;

    private Route currentEditingRoute;
    private bool isEditing = false;

    void Start()
    {
        ShowRouteList();
        createRouteBtn.onClick.AddListener(OnCreateNewRoute);
        closeEditorBtn.onClick.AddListener(ShowRouteList);
    }

    void Update()
    {
        // Handling "Click to Add Stop" logic
        if (isEditing && currentEditingRoute != null)
        {
            if (Input.GetMouseButtonDown(0))
            {
                TryAddStopFromMouse();
            }
        }
    }

    // --- UI STATES ---

    void ShowRouteList()
    {
        isEditing = false;
        visualizer.Clear();
        TransportManager.Instance.SaveRoutes(); // Save when exiting editor

        routeListPanel.SetActive(true);
        routeEditorPanel.SetActive(false);

        // Rebuild List
        foreach (Transform child in routeListContent) Destroy(child.gameObject);

        foreach (var route in TransportManager.Instance.ActiveRoutes)
        {
            GameObject item = Instantiate(routeListItemPrefab, routeListContent);
            item.GetComponentInChildren<Text>().text = $"{route.RouteName} ({route.StopIDs.Count} stops)";
            item.GetComponent<Button>().onClick.AddListener(() => StartEditingRoute(route));
        }
    }

    void StartEditingRoute(Route route)
    {
        currentEditingRoute = route;
        isEditing = true;
        
        routeListPanel.SetActive(false);
        routeEditorPanel.SetActive(true);

        RefreshEditorUI();
    }

    void OnCreateNewRoute()
    {
        Route newRoute = TransportManager.Instance.CreateRoute("New Route", Random.ColorHSV(0f, 1f, 1f, 1f, 0.8f, 1f));
        StartEditingRoute(newRoute);
    }

    // --- EDITING LOGIC ---

    void RefreshEditorUI()
    {
        if (currentEditingRoute == null) return;

        routeNameInput.text = currentEditingRoute.RouteName;
        stopCountText.text = $"Stops: {currentEditingRoute.StopIDs.Count} (Click Bus Stops in Scene to Add)";
        
        // Preview Path
        visualizer.DrawRoute(currentEditingRoute);
    }

    public void OnNameChanged(string newName)
    {
        if (currentEditingRoute != null) currentEditingRoute.RouteName = newName;
    }

    void TryAddStopFromMouse()
    {
        // Raycast for BusStops
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            BusStop stop = hit.collider.GetComponent<BusStop>();
            if (stop == null) stop = hit.collider.GetComponentInParent<BusStop>();

            if (stop != null)
            {
                // Add stop to route
                currentEditingRoute.StopIDs.Add(stop.stopID);
                Debug.Log($"Added Stop {stop.name} to route.");
                RefreshEditorUI();
            }
        }
    }
}