using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI card for a single bus stop in the route edit panel.
/// Shows stop name, order number, and a delete button.
/// </summary>
public class BusStopCardDisplay : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text stopNameText;
    public TMP_Text orderNumberText;
    public Button deleteButton;

    private string _stopID;
    private Action<string> _onDelete;

    public string StopID => _stopID;

    public void Setup(string stopID, string displayName, int orderNumber, Action<string> onDelete)
    {
        _stopID = stopID;
        _onDelete = onDelete;

        if (stopNameText != null)
            stopNameText.text = displayName;

        if (orderNumberText != null)
            orderNumberText.text = orderNumber.ToString();

        if (deleteButton != null)
        {
            deleteButton.onClick.RemoveAllListeners();
            deleteButton.onClick.AddListener(OnDeletePressed);
        }
    }

    private void OnDeletePressed()
    {
        _onDelete?.Invoke(_stopID);
    }
}
