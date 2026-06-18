using TMPro;
using UnityEngine;

public class PendingOrderCardUI : MonoBehaviour
{
    public TextMeshProUGUI itemText;
    public TextMeshProUGUI statusText;

    private ActiveOrder _order;

    public void Setup(ActiveOrder order)
    {
        _order = order;
        itemText.text = order.ItemID; // This naturally handles "StandardTire4-13" from our previous update
        itemText.color = Color.white;
    }

    private void Update()
    {
        if (_order == null || SimulationTimeManager.Instance == null) return;

        float currentAbsHour = SimulationTimeManager.Instance.CurrentDay * 24f + SimulationTimeManager.Instance.CurrentTimeOfDay;

        if (currentAbsHour < _order.ExpectedArrivalHour)
        {
            float hoursLeft = Mathf.Max(0f, _order.ExpectedArrivalHour - currentAbsHour);
            statusText.text = $"Est: {hoursLeft:F1}h";
            statusText.color = Color.white;
        }
        else
        {
            if (_order.IsDelayed)
            {
                float delayLeft = Mathf.Max(0f, _order.ActualArrivalHour - currentAbsHour);
                statusText.text = $"DELAYED! {delayLeft:F1}h left";
                statusText.color = Color.red;
            }
            else
            {
                statusText.text = "Arriving...";
                statusText.color = Color.green;
            }
        }
    }
}