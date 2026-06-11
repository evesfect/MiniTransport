using UnityEngine;
using System.Collections.Generic;

public class PriorityListManager : MonoBehaviour
{
    [Header("References")]
    public GameObject draggablePartPrefab;
    public Transform listContainer;

    private void OnEnable()
    {
        PopulateList();
    }

    private void PopulateList()
    {
        // Clear existing UI items
        foreach (Transform child in listContainer)
        {
            Destroy(child.gameObject);
        }

        if (MaintenanceManager.Instance == null) return;

        // Spawn a UI row for every part in the current priority order
        foreach (BusPartType partType in MaintenanceManager.Instance.repairPriority)
        {
            GameObject newRow = Instantiate(draggablePartPrefab, listContainer);
            DraggablePartItem itemScript = newRow.GetComponent<DraggablePartItem>();
            itemScript.Setup(partType, this);
        }
    }

    // Called by the DraggablePartItem when the player lets go of the mouse
    public void OnListReordered()
    {
        if (MaintenanceManager.Instance == null) return;

        // CRITICAL FIX: Grab only the actual components, completely ignoring the dying placeholder!
        DraggablePartItem[] activeItems = listContainer.GetComponentsInChildren<DraggablePartItem>();

        BusPartType[] newPriorityArray = new BusPartType[activeItems.Length];

        for (int i = 0; i < activeItems.Length; i++)
        {
            newPriorityArray[i] = activeItems[i].PartType;
        }

        // Send the clean, correctly sized array to the backend!
        MaintenanceManager.Instance.UpdateRepairPriorityRpc(newPriorityArray);
    }
}