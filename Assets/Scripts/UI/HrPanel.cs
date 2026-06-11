using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class HRDashboardUI : MonoBehaviour
{
    [Header("Staff Listing")]
    public Transform staffListContainer;
    public GameObject employeeRowPrefab;

    [Header("Candidate Lobby")]
    public Transform candidatesContainer;
    public GameObject candidateCardPrefab;

    [Header("Campaign Controls")]
    public Button flyersCampaignBtn;
    public Button classifiedsCampaignBtn;
    public Button headhunterCampaignBtn;
    public TMP_Text campaignStatusText;

    private Coroutine _refreshRoutine;

    private void OnEnable()
    {
        if (_refreshRoutine != null) StopCoroutine(_refreshRoutine);
        _refreshRoutine = StartCoroutine(WaitForDataAndInitialize());
    }

    private void OnDisable()
    {
        if (_refreshRoutine != null)
        {
            StopCoroutine(_refreshRoutine);
            _refreshRoutine = null;
        }

        if (EmployeeManager.Instance != null)
        {
            // Unsubscribe safely
            EmployeeManager.Instance.OnEmployeeHired -= (id) => RefreshUI();
            EmployeeManager.Instance.OnEmployeeFired -= (id) => RefreshUI();
            EmployeeManager.Instance.OnEmployeeTrained -= (id, skill) => RefreshUI();
            EmployeeManager.Instance.OnCandidatesUpdated -= RefreshUI;
        }
    }

    private IEnumerator WaitForDataAndInitialize()
    {
        while (EmployeeManager.Instance == null) yield return null;

        // Register HR listeners securely using lambda expressions to ignore unused parameters
        EmployeeManager.Instance.OnEmployeeHired += (id) => RefreshUI();
        EmployeeManager.Instance.OnEmployeeFired += (id) => RefreshUI();
        EmployeeManager.Instance.OnEmployeeTrained += (id, skill) => RefreshUI();

        // OnCandidatesUpdated is an Action (0 parameters), so it matches RefreshUI directly
        EmployeeManager.Instance.OnCandidatesUpdated += RefreshUI;

        // Setup marketing buttons
        if (flyersCampaignBtn != null)
            flyersCampaignBtn.onClick.AddListener(() => EmployeeManager.Instance.LaunchAdCampaign(AdTier.Flyers));

        if (classifiedsCampaignBtn != null)
            classifiedsCampaignBtn.onClick.AddListener(() => EmployeeManager.Instance.LaunchAdCampaign(AdTier.Classifieds));

        if (headhunterCampaignBtn != null)
            headhunterCampaignBtn.onClick.AddListener(() => EmployeeManager.Instance.LaunchAdCampaign(AdTier.Headhunter));

        RefreshUI();
        _refreshRoutine = null;
    }

    private void RefreshAll(string unusedID = "") => RefreshUI();
    private void RefreshOnTrained(string unusedID, float unusedSkill) => RefreshUI();

    public void RefreshUI()
    {
        if (EmployeeManager.Instance == null) return;

        // 1. Update Staff Directory
        foreach (Transform child in staffListContainer) Destroy(child.gameObject);

        foreach (var emp in EmployeeManager.Instance.allEmployees)
        {
            GameObject rowObj = Instantiate(employeeRowPrefab, staffListContainer);
            if (rowObj.TryGetComponent<HREmployeeRowUI>(out var rowUI))
            {
                rowUI.Setup(emp);
            }
        }

        // 2. Update Candidate Lobby Cards
        foreach (Transform child in candidatesContainer) Destroy(child.gameObject);

        for (int i = 0; i < EmployeeManager.Instance.candidates.Count; i++)
        {
            int candidateIndex = i; // Local copy for safe delegate execution
            var applicant = EmployeeManager.Instance.candidates[i];

            GameObject cardObj = Instantiate(candidateCardPrefab, candidatesContainer);
            if (cardObj.TryGetComponent<HRCandidateCardUI>(out var cardUI))
            {
                cardUI.Setup(applicant, candidateIndex);
            }
        }

        // 3. Update Visual Marketing Status Banner
        if (campaignStatusText != null)
        {
            if (EmployeeManager.Instance.IsCampaignActive)
            {
                campaignStatusText.text = $"STATUS: {EmployeeManager.Instance.CurrentCampaignTier.ToString().ToUpper()} CAMPAIGN ACTIVE\n(Applicants arriving tomorrow morning)";
                campaignStatusText.color = new Color(1f, 0.6f, 0f); // Warm warning orange
            }
            else
            {
                campaignStatusText.text = "STATUS: NO ACTIVE CAMPAIGNS\n(Lobby populated with standard/legacy recruits)";
                campaignStatusText.color = Color.gray;
            }
        }
    }
}