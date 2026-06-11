using UnityEngine;
using TMPro;
using System.Collections;

public class BusScrollManager : BaseScrollManager<BusData>
{
    [Header("Bus Detail References")]
    public GameObject detailContainer;
    public TMP_Text detailTitleText;
    public TMP_Text detailHealthText;
    public TMP_Text detailStatusText;
    public TMP_Text detailPartsText;

    private Coroutine _refreshRoutine;

    private void OnEnable()
    {
        if (FleetManager.Instance != null)
        {
            FleetManager.Instance.OnFleetUpdated += RefreshList;
        }

        if (_refreshRoutine != null)
        {
            StopCoroutine(_refreshRoutine);
        }

        _refreshRoutine = StartCoroutine(WaitForFleetDataAndRefresh());
    }

    private void OnDisable()
    {
        if (FleetManager.Instance != null)
        {
            FleetManager.Instance.OnFleetUpdated -= RefreshList;
        }

        if (_refreshRoutine != null)
        {
            StopCoroutine(_refreshRoutine);
            _refreshRoutine = null;
        }
    }

    void Start()
    {
        if (detailContainer != null)
        {
            detailContainer.SetActive(false);
        }
    }

    private void RefreshList()
    {
        if (FleetManager.Instance != null && FleetManager.Instance.allBuses != null)
        {
            activeItems.Clear();
            activeItems.AddRange(FleetManager.Instance.allBuses);
            PopulateList();
            Debug.Log($"[BusScrollManager] Listed {activeItems.Count} buses from FleetManager.");
        }
    }

    private IEnumerator WaitForFleetDataAndRefresh()
    {
        // Give network/load flow time to populate FleetManager on first open.
        const float timeout = 2f;
        float elapsed = 0f;

        while ((FleetManager.Instance == null || FleetManager.Instance.allBuses.Count == 0) && elapsed < timeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        RefreshList();
        _refreshRoutine = null;
    }

    protected override void SetupItemDisplay(GameObject instantiatedPrefab, BusData itemData)
    {
        BusCardDisplay cardDisplay = instantiatedPrefab.GetComponent<BusCardDisplay>();
        if (cardDisplay != null)
        {
            cardDisplay.Setup(itemData, DisplayBusDetails);
        }
    }

    private void DisplayBusDetails(BusData busData)
    {
        if (detailContainer != null)
        {
            detailContainer.SetActive(true);
        }

        detailTitleText.text = $"Bus ID: {busData.BusID}";
        detailHealthText.text = $"Overall Health: {busData.GetAverageHealth():F1}%";

        if (busData.Schedule != null && !string.IsNullOrEmpty(busData.Schedule.RouteID))
        {
            detailStatusText.text = $"Status: Active on {busData.Schedule.RouteID}";
        }
        else if (!string.IsNullOrEmpty(busData.AssignedDepotID))
        {
            detailStatusText.text = $"Status: Parked in {busData.AssignedDepotID}";
        }
        else
        {
            detailStatusText.text = "Status: Unassigned";
        }

        if (detailPartsText != null)
        {
            string partsBreakdown = "<b>Parts Inspection:</b>\n";
            foreach (BusPartData part in busData.Parts)
            {
                partsBreakdown += $"• {part.PartType}: {part.Health:F0} / {part.MaxLife:F0}\n";
            }
            detailPartsText.text = partsBreakdown;
        }
    }
}