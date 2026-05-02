using UnityEngine;
using UnityEngine.UI;

public class MaintenanceDashboardUI : MonoBehaviour
{
    [Header("Threshold Sliders")]
    public Slider operationalSlider;
    public Slider replacementSlider;

    private void OnEnable()
    {
        // 1. Fetch the actual current backend values the moment the menu opens!
        if (MaintenanceManager.Instance != null)
        {
           
            operationalSlider.value = MaintenanceManager.Instance.operationalThreshold;
            replacementSlider.value = MaintenanceManager.Instance.replacePartThreshold;
        }

        
        
        operationalSlider.onValueChanged.AddListener(OnSliderChanged);
        replacementSlider.onValueChanged.AddListener(OnSliderChanged);
    }

    private void OnDisable()
    {
        
        
        operationalSlider.onValueChanged.RemoveListener(OnSliderChanged);
        replacementSlider.onValueChanged.RemoveListener(OnSliderChanged);
    }

   
    private void OnSliderChanged(float _)
    {
        if (MaintenanceManager.Instance != null)
        {
            // Send the state of all three sliders to the Server/Host
            MaintenanceManager.Instance.UpdateThresholdsRpc(
                
                operationalSlider.value,
                replacementSlider.value
            );
        }
    }
}