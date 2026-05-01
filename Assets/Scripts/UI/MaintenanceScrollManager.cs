using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MaintenanceQueueScrollManager : MonoBehaviour
{
    [Header("References")]
    public Transform contentContainer;
    public GameObject maintenanceItemPrefab; // Assign your NEW prefab here

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
        var queue = MaintenanceManager.Instance != null
            ? MaintenanceManager.Instance.WorkQueue
            : (IReadOnlyList<WorkItem>)new List<WorkItem>();

        // 1. Grow pool and subscribe to your existing Drag Handler
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

        // 2. Populate slots using the NEW card display
        for (int i = 0; i < queue.Count; i++)
        {
            _pool[i].SetActive(true);
            _pool[i].transform.SetSiblingIndex(i);

            var card = _pool[i].GetComponent<MaintenanceQueueCardDisplay>();
            if (card != null)
            {
                var workItem = queue[i];

                // Fetch the Capacity Demand
                float capacityCost = 0f;
                if (MaintenanceManager.Instance != null)
                {
                    capacityCost = MaintenanceManager.Instance.GetMaxCapacityAllowance(workItem.IssuePartType);
                }

                card.Setup(workItem, HandlePrioritize, capacityCost);
            }
        }

        // 3. Hide unused slots
        for (int i = queue.Count; i < _pool.Count; i++)
            _pool[i].SetActive(false);
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