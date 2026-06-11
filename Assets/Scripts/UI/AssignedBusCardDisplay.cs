using TMPro;
using UnityEngine;

/// <summary>
/// Simple display card for a bus assigned to a route in the edit panel.
/// </summary>
public class AssignedBusCardDisplay : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text busIDText;
    public TMP_Text capacityText;
    public TMP_Text healthText;

    public void Setup(BusData busData)
    {
        if (busIDText != null)
            busIDText.text = busData.BusID;

        if (capacityText != null)
            capacityText.text = busData.Capacity.ToString();

        if (healthText != null)
            healthText.text = $"{busData.GetAverageHealth():F0}%";
    }
}
