using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One row in the reusable KPI drill-down list. Data-driven: fed a single
/// <see cref="KpiDetailEntry"/> by <see cref="KpiDetailPanelUI"/>.
/// </summary>
public class KpiDetailRowUI : MonoBehaviour
{
    [Header("Texts")]
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private TMP_Text valueText;   // optional (entries with value == 0 hide it)
    [SerializeField] private TMP_Text timeText;

    [Header("Optional Colour Target")]
    [Tooltip("If set, its colour is tinted by entry kind. Otherwise the value text colour is used.")]
    [SerializeField] private Image background;

    // Chosen to read clearly on a white background (no washed-out grey).
    private static readonly Color Positive = new Color(0.18f, 0.49f, 0.20f); // green  (#2E7D32)
    private static readonly Color Negative = new Color(0.78f, 0.16f, 0.16f); // red    (#C62828)
    private static readonly Color Neutral  = new Color(0.08f, 0.40f, 0.75f); // blue   (#1565C0)

    public void Setup(KpiDetailEntry e)
    {
        Color tint = e.kind == 1 ? Positive : e.kind == 2 ? Negative : Neutral;

        if (labelText != null) labelText.text = e.label;

        if (valueText != null)
        {
            // Hide the value column for entries that carry no magnitude (e.g. breakdowns).
            bool hasValue = !Mathf.Approximately(e.value, 0f);
            valueText.gameObject.SetActive(hasValue);
            if (hasValue) valueText.text = $"{(e.value >= 0f ? "+" : "")}{e.value:0.##}";
            valueText.color = tint;
        }

        if (timeText != null)
        {
            // day <= 0 means "no timestamp" (live entity lists) → hide the column.
            bool hasStamp = e.day > 0;
            timeText.gameObject.SetActive(hasStamp);
            if (hasStamp)
            {
                if (e.timeOfDay > 0f)
                {
                    int hh = Mathf.FloorToInt(e.timeOfDay);
                    int mm = Mathf.FloorToInt((e.timeOfDay - hh) * 60f);
                    timeText.text = $"Day {e.day}  {hh:00}:{mm:00}";
                }
                else
                {
                    timeText.text = $"Day {e.day}";  // ledger entries carry only the day
                }
            }
        }

        if (background != null) background.color = tint;
    }
}
