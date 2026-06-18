using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class MaintenanceQueueScrollManager : MonoBehaviour
{
    [Header("References")]
    public Transform contentContainer;
    public GameObject maintenanceItemPrefab; // Assign your NEW prefab here
    
    [Header("Popup Reference")]
    public MaintenanceInfoPopup infoPopupPanel;

    private readonly List<GameObject> _pool = new List<GameObject>();

    private void Start()
    {
        var sr = GetComponentInParent<ScrollRect>();
        if (sr != null)
        {
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = false;
            sr.horizontal = false;
        }

        if (MaintenanceManager.Instance != null)
            MaintenanceManager.Instance.OnWorkQueueChanged += Refresh;

        Refresh();
    }

    private void OnDestroy()
    {
        if (MaintenanceManager.Instance != null)
            MaintenanceManager.Instance.OnWorkQueueChanged -= Refresh;
    }

    private void Refresh()
    {
        // 1. Fetch items, filtering out any buses currently broken down out on the road
        var queue = MaintenanceManager.Instance != null
            ? MaintenanceManager.Instance.WorkQueue
                .Where(w => !MaintenanceManager.Instance.IsOnRouteBreakdown(w.BusID))
                .ToList()
            : new List<WorkItem>();

        // 2. Grow pool and subscribe to your existing Drag Handler
        while (_pool.Count < queue.Count)
        {
            var go = Instantiate(maintenanceItemPrefab, contentContainer);
            go.SetActive(false);

            // We safely reuse your existing drag handler!
            var drag = go.GetComponent<WorkItemDragHandler>();
            if (drag != null)
                drag.OnOrderChanged += CommitDragOrder;

            _pool.Add(go);
        }

        // 3. Populate slots using the card display
        for (int i = 0; i < queue.Count; i++)
        {
            _pool[i].SetActive(true);
            _pool[i].transform.SetSiblingIndex(i);

            var card = _pool[i].GetComponent<MaintenanceQueueCardDisplay>();
            if (card != null)
            {
                var workItem = queue[i];

                // Fetch the Capacity Demand
                float totalCapacityCost = 0f;

                // Calculate the TOTAL demand for the entire bus
                if (MaintenanceManager.Instance != null && FleetManager.Instance != null)
                {
                    var bus = FleetManager.Instance.allBuses.FirstOrDefault(b => b.BusID == workItem.BusID);
                    if (bus != null)
                    {
                        float replaceThreshold = MaintenanceManager.Instance.replacePartThreshold;

                        foreach (var part in bus.Parts)
                        {
                            // Check if the part actually needs repair
                            if (part.Health < part.MaxLife || part.MaxLife < replaceThreshold)
                            {
                                totalCapacityCost += MaintenanceManager.Instance.GetMaxCapacityAllowance(part.PartType);
                            }
                        }
                    }
                }

                card.Setup(workItem, HandlePrioritize, HandleInfoClick, totalCapacityCost);
            }
        }

        // 4. Hide unused slots
        for (int i = queue.Count; i < _pool.Count; i++)
        {
            _pool[i].SetActive(false);
        }
    }

    private void HandleInfoClick(WorkItem item)
    {
        if (infoPopupPanel != null)
        {
            infoPopupPanel.Show(item);
        }
    }

    private void CommitDragOrder()
    {
        var orderedIDs = new List<string>();
        for (int i = 0; i < contentContainer.childCount; i++)
        {
            var child = contentContainer.GetChild(i);
            if (!child.gameObject.activeSelf) continue;

            // Look for the NEW card component
            var card = child.GetComponent<MaintenanceQueueCardDisplay>();
            if (card?.CurrentItem != null)
                orderedIDs.Add(card.CurrentItem.WorkItemID);
        }

        if (orderedIDs.Count > 0 && MaintenanceManager.Instance != null)
            MaintenanceManager.Instance.ReorderWorkQueue(orderedIDs);
    }

    private void HandlePrioritize(WorkItem item)
    {
        if (MaintenanceManager.Instance != null)
            MaintenanceManager.Instance.PrioritizeWorkItem(item.WorkItemID);
    }
}