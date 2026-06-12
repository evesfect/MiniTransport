using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SellBusCardUI : MonoBehaviour
{
    [Header("Text Fields")]
    public TextMeshProUGUI idText;
    public TextMeshProUGUI durabilityText;

    [Header("Controls")]
    public Toggle selectToggle;

    public string BusID { get; private set; }

    // Parent panel will subscribe to this to listen for toggle changes
    public event Action<SellBusCardUI> OnCardToggled;

    private void Start()
    {
        // Listen for internal toggle changes and pass this card to parent
        selectToggle.onValueChanged.AddListener((isOn) => OnCardToggled?.Invoke(this));
    }

    public void Setup(BusData bus)
    {
        BusID = bus.BusID;

        idText.text = $"ID: {bus.BusID}";

        // Read durability from the fleet data instead of a removed runtime lookup.
        if (FleetManager.Instance != null)
        {
            BusData fleetBus = FleetManager.Instance.allBuses.FirstOrDefault(b => b.BusID == bus.BusID);
            if (fleetBus != null)
            {
                float averageHealth = fleetBus.GetAverageHealth();
                durabilityText.text = $"Durability: {averageHealth:F0}%";
            }
            else
            {
                durabilityText.text = "Durability: ??%";
            }
        }
        else
        {
            durabilityText.text = "Durability: ??%";
        }

        // Reset toggle when spawned
        selectToggle.isOn = false;
    }

    // Force toggle state (used for limiting selection)
    public void SetToggleState(bool state, bool sendEvent = true)
    {
        if (sendEvent)
        {
            selectToggle.isOn = state;
        }
        else
        {
            // Silently change without invoking listener
            selectToggle.SetIsOnWithoutNotify(state);
        }
    }

    public bool IsSelected => selectToggle.isOn;
}