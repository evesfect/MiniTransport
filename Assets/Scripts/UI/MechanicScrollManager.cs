using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class MechanicScrollManager : MonoBehaviour
{
    [Header("Hierarchy References")]
    [SerializeField] private Transform scrollContentParent; 
    [SerializeField] private MechanicDetailPanel detailPanel; 

    [Header("Prefabs")]
    [SerializeField] private GameObject mechanicCardPrefab;

    private Coroutine _refreshRoutine;

    private void OnEnable()
    {
        if (_refreshRoutine != null)
        {
            StopCoroutine(_refreshRoutine);
        }

        _refreshRoutine = StartCoroutine(WaitForEmployeeDataAndRefresh());
    }

    private void OnDisable()
    {
        if (_refreshRoutine != null)
        {
            StopCoroutine(_refreshRoutine);
            _refreshRoutine = null;
        }
    }

    private IEnumerator WaitForEmployeeDataAndRefresh()
    {
        // Give network/load flow time to populate EmployeeManager on first open.
        const float timeout = 2f;
        float elapsed = 0f;

        while ((EmployeeManager.Instance == null || EmployeeManager.Instance.allEmployees == null) && elapsed < timeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        RefreshList();
        _refreshRoutine = null;
    }

    public void RefreshList()
    {
        if (scrollContentParent == null)
        {
            Debug.LogError("[MechanicScrollManager] Scroll Content Parent is not assigned.");
            return;
        }

        if (mechanicCardPrefab == null)
        {
            Debug.LogError("[MechanicScrollManager] Mechanic Card Prefab is not assigned.");
            return;
        }

        ClearList();
        PopulateMechanicsList();
    }

    private void ClearList()
    {
        foreach (Transform child in scrollContentParent)
        {
            Destroy(child.gameObject);
        }
    }

    private void PopulateMechanicsList()
    {
        if (EmployeeManager.Instance == null || EmployeeManager.Instance.allEmployees == null)
        {
            Debug.LogError("EmployeeManager is missing or data is not loaded!");
            return;
        }

        int shownCount = 0;

        foreach (EmployeeData employee in EmployeeManager.Instance.allEmployees)
        {
            // Only spawn cards for Mechanics (Role == 1 in your enum)
            if (employee.Role == EmployeeRole.Mechanic)
            {
                CreateMechanicCard(employee);
                shownCount++;
            }
        }

        Debug.Log($"[MechanicScrollManager] Listed {shownCount} mechanics out of {EmployeeManager.Instance.allEmployees.Count} employees.");
    }

    private void CreateMechanicCard(EmployeeData employeeData)
    {
        GameObject newCard = Instantiate(mechanicCardPrefab, scrollContentParent);
        
        if (newCard.TryGetComponent(out MechanicCardDisplay cardDisplay))
        {
            cardDisplay.Populate(employeeData, OnMechanicInfoClicked);
        }
        else
        {
            Debug.LogError("MechanicCard prefab is missing MechanicCardDisplay script!");
        }
    }

    private void OnMechanicInfoClicked(EmployeeData selectedEmployee)
    {
        if (detailPanel == null)
        {
            Debug.LogWarning("[MechanicScrollManager] Detail panel is not assigned.");
            return;
        }

        detailPanel.PopulateDetailView(selectedEmployee);
    }
}