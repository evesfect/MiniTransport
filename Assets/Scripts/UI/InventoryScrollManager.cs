using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class InventoryScrollManager : BaseScrollManager<InventoryItemData>
{
    private Coroutine _refreshRoutine;

    private void OnEnable()
    {
        if (_refreshRoutine != null)
        {
            StopCoroutine(_refreshRoutine);
        }

        _refreshRoutine = StartCoroutine(WaitForInventoryDataAndRefresh());
    }

    private void OnDisable()
    {
        if (_refreshRoutine != null)
        {
            StopCoroutine(_refreshRoutine);
            _refreshRoutine = null;
        }
    }

    private IEnumerator WaitForInventoryDataAndRefresh()
    {
        // Give network/load flow time to populate InventoryManager on first open.
        const float timeout = 2f;
        float elapsed = 0f;

        while ((InventoryManager.Instance == null || InventoryManager.Instance.allAvailableItems == null) && elapsed < timeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (InventoryManager.Instance != null && InventoryManager.Instance.allAvailableItems != null)
        {
            activeItems.Clear();

            // Add the actual live data from the backend
            activeItems.AddRange(InventoryManager.Instance.allAvailableItems);

            PopulateList();
            Debug.Log($"[InventoryScrollManager] Listed {activeItems.Count} items from InventoryManager.");
        }
        else
        {
            Debug.LogError("[InventoryScrollManager] InventoryManager is missing or data is not loaded within timeout!");
        }

        _refreshRoutine = null;
    }

    protected override void SetupItemDisplay(GameObject instantiatedPrefab, InventoryItemData itemData)
    {
        // Get the specific inventory card script
        InventoryCardDisplay cardDisplay = instantiatedPrefab.GetComponent<InventoryCardDisplay>();

        if (cardDisplay != null)
        {
            // Just pass the data to the card, no callbacks needed
            cardDisplay.Setup(itemData);
        }
        else
        {
            Debug.LogWarning("InventoryCardDisplay script is missing on the Inventory Item Prefab!");
        }
    }
}