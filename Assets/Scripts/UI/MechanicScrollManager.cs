using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

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
        if (_refreshRoutine != null) StopCoroutine(_refreshRoutine);
        _refreshRoutine = StartCoroutine(WaitForEmployeeDataAndRefresh());
    }

    private void OnDisable()
    {
        if (_refreshRoutine != null)
        {
            StopCoroutine(_refreshRoutine);
            _refreshRoutine = null;
        }

        if (EmployeeManager.Instance != null)
            EmployeeManager.Instance.OnEmployeeDataUpdated -= RefreshList;
    }

    private IEnumerator WaitForEmployeeDataAndRefresh()
    {
        const float timeout = 2f;
        float elapsed = 0f;

        while ((EmployeeManager.Instance == null || EmployeeManager.Instance.allEmployees == null) && elapsed < timeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        RefreshList();

        // Rebuild the list whenever staff data changes (e.g. a team assignment from the detail panel).
        if (EmployeeManager.Instance != null)
        {
            EmployeeManager.Instance.OnEmployeeDataUpdated -= RefreshList;
            EmployeeManager.Instance.OnEmployeeDataUpdated += RefreshList;
        }

        _refreshRoutine = null;
    }

    public void RefreshList()
    {
        if (scrollContentParent == null || mechanicCardPrefab == null) return;

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
        if (EmployeeManager.Instance == null || EmployeeManager.Instance.allEmployees == null) return;

        int shownCount = 0;

        // Sort by Depot first, then by NORMALIZED Team ID so crew members sit adjacent to one another
        var sortedMechanics = EmployeeManager.Instance.allEmployees
            .Where(e => e.Role == EmployeeRole.Mechanic)
            .OrderBy(e => e.AssignedDepotID)
            .ThenBy(e => string.IsNullOrEmpty(e.AssignedTeamID) ? "General Crew" : e.AssignedTeamID)
            .ToList();

        foreach (EmployeeData employee in sortedMechanics)
        {
            CreateMechanicCard(employee);
            shownCount++;
        }
    }

    private void CreateMechanicCard(EmployeeData employeeData)
    {
        GameObject newCard = Instantiate(mechanicCardPrefab, scrollContentParent);

        if (newCard.TryGetComponent(out MechanicCardDisplay cardDisplay))
        {
            cardDisplay.Populate(employeeData, OnMechanicInfoClicked);
        }
    }

    private void OnMechanicInfoClicked(EmployeeData selectedEmployee)
    {
        if (detailPanel != null)
        {
            detailPanel.PopulateDetailView(selectedEmployee);
        }
    }
}