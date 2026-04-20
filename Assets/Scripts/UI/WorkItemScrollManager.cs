using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WorkItemScrollManager : MonoBehaviour
{
    [Header("References")]
    public Transform contentContainer;
    public GameObject itemPrefab;

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

        // Grow pool — subscribe drag handler once per new slot
        while (_pool.Count < queue.Count)
        {
            var go = Instantiate(itemPrefab, contentContainer);
            go.SetActive(false);

            var drag = go.GetComponent<WorkItemDragHandler>();
            if (drag != null)
                drag.OnOrderChanged += CommitDragOrder;

            _pool.Add(go);
        }

        // Populate slots in data order, enforcing sibling index
        for (int i = 0; i < queue.Count; i++)
        {
            _pool[i].SetActive(true);
            _pool[i].transform.SetSiblingIndex(i);
            var card = _pool[i].GetComponent<WorkItemCardDisplay>();
            if (card != null)
                card.Setup(queue[i], HandlePrioritize);
        }

        // Hide unused slots
        for (int i = queue.Count; i < _pool.Count; i++)
            _pool[i].SetActive(false);
    }

    // Called after a drag drop — reads visual order and commits it to the manager
    private void CommitDragOrder()
    {
        var orderedIDs = new List<string>();
        for (int i = 0; i < contentContainer.childCount; i++)
        {
            var child = contentContainer.GetChild(i);
            if (!child.gameObject.activeSelf) continue;
            var card = child.GetComponent<WorkItemCardDisplay>();
            if (card?.CurrentItem != null)
                orderedIDs.Add(card.CurrentItem.WorkItemID);
        }

        if (orderedIDs.Count > 0)
            MaintenanceManager.Instance?.ReorderWorkQueue(orderedIDs);
    }

    private void HandlePrioritize(WorkItem item)
    {
        if (MaintenanceManager.Instance != null)
            MaintenanceManager.Instance.PrioritizeWorkItem(item.WorkItemID);
    }
}
