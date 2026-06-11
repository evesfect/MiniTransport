using UnityEngine;
using TMPro;

public class MechanicUIRow : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text nameText;
    public TMP_Text skillText;

    // Called by the main dashboard when it spawns this row
    public void Setup(string mechanicName, float skillLevel)
    {
        if (nameText != null) nameText.text = mechanicName;

        // Formats the skill to 1 decimal place, e.g., "Capacity: 15.0"
        if (skillText != null) skillText.text = $"Capacity: {skillLevel:F1}";
    }
}