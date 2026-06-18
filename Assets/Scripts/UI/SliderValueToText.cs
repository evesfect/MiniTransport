using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Slider))] // This forces Unity to make sure a Slider is attached
public class SliderValueToText : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Drag your TextMeshPro text object here")]
    public TMP_Text valueText;

    [Header("Formatting")]
    [Tooltip("What comes after the number? (e.g., %, HP, L)")]
    public string suffix = "%";

    [Tooltip("0 = Whole numbers only. 1 = One decimal place, etc.")]
    public int decimalPlaces = 0;

    private Slider _slider;

    private void Awake()
    {
        _slider = GetComponent<Slider>();

        // 1. Update the text immediately so it's correct the second the game starts
        UpdateText(_slider.value);

        // 2. Subscribe to the slider's built-in event!
        _slider.onValueChanged.AddListener(UpdateText);
    }

    private void UpdateText(float val)
    {
        if (valueText != null)
        {
            // "F0" formats it to 0 decimals (e.g., 50). "F1" formats to 1 decimal (e.g., 50.2)
            string formatString = "F" + decimalPlaces;

            valueText.text = val.ToString(formatString) + suffix;
        }
    }

    private void OnDestroy()
    {
        // 3. Always unsubscribe when destroyed to prevent memory leaks!
        if (_slider != null)
        {
            _slider.onValueChanged.RemoveListener(UpdateText);
        }
    }
}