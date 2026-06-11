using UnityEngine;
using UnityEngine.UI;

public class MaintenanceRequestUI : MonoBehaviour
{
    [Header("Navigation Buttons")]
    public Button hrButton;
    public Button financePartsButton; 
    public Button sellBusButton; 

    [Header("Sub-Containers (inside SubViewsContainer)")]
    public GameObject hrContainer;
    public GameObject financePartsContainer;
    public GameObject sellBusContainer; 

    private void Start()
    {
        // Subscribe to standard button events
        hrButton.onClick.AddListener(() => ChangeView(hrContainer));
        financePartsButton.onClick.AddListener(() => ChangeView(financePartsContainer));
        sellBusButton.onClick.AddListener(() => ChangeView(sellBusContainer));

        // Set initial state (HR sub-view open, others closed)
        ChangeView(hrContainer);
    }

    private void ChangeView(GameObject activeContainer)
    {
        hrContainer.SetActive(false);
        financePartsContainer.SetActive(false);
        sellBusContainer.SetActive(false);

        activeContainer.SetActive(true);
    }
}