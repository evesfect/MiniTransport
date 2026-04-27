using System.Collections.Generic;
using UnityEngine;

public abstract class BaseScrollManager<T> : MonoBehaviour
{
    [Header("Base List References")]
    public Transform contentContainer;
    public GameObject itemPrefab;

    // The list of generic items
    public List<T> activeItems = new List<T>();

    public virtual void PopulateList()
    {
        // 1. Clear existing items
        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }

        // 2. Instantiate new items
        foreach (T item in activeItems)
        {
            GameObject newItem = Instantiate(itemPrefab, contentContainer);

            // 3. Let the specific child class handle hooking up the UI
            SetupItemDisplay(newItem, item);
        }
    }

  
    protected abstract void SetupItemDisplay(GameObject instantiatedPrefab, T itemData);
}