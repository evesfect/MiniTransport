using UnityEngine;
using TMPro;

public class BusScrollManager : BaseScrollManager<BusData>
{
    [Header("Bus Detail References")]
    public GameObject detailContainer;
    public TMP_Text detailTitleText;
    public TMP_Text detailHealthText;
    public TMP_Text detailStatusText;
    public TMP_Text detailPartsText;

    void Start()
    {
        if (detailContainer != null)
        {
            detailContainer.SetActive(false);
        }

        GenerateMockData();

        
        PopulateList();
    }

    private void GenerateMockData()
    {
        
        activeItems.Clear();

        BusData bus1 = new BusData { BusID = "B-101", AssignedDepotID = "Depot-A", Capacity = 40 };
        bus1.InitializeParts();
        bus1.Schedule = new BusSchedule { RouteID = "Route 42" };
        activeItems.Add(bus1);

        BusData bus2 = new BusData { BusID = "B-102", AssignedDepotID = "Depot-B", Capacity = 30 };
        bus2.InitializeParts();
        bus2.Parts[0].Health = 40f;
        bus2.Parts[3].Health = 60f;
        bus2.Parts[0].MaxLife = 85f;
        activeItems.Add(bus2);

        BusData bus3 = new BusData { BusID = "B-103", Capacity = 50 };
        bus3.InitializeParts();
        foreach (var part in bus3.Parts)
        {
            part.Health = 15f;
            part.MaxLife = 50f;
        }
        activeItems.Add(bus3);

        BusData bus4 = new BusData { BusID = "B-104", AssignedDepotID = "Depot-A", Capacity = 60 };
        bus4.InitializeParts();
        bus4.Parts[2].Health = 80f;
        bus4.Schedule = new BusSchedule { RouteID = "Route 15" };
        activeItems.Add(bus4);
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