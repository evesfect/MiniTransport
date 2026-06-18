using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Reusable, data-driven drill-down dashboard shown when a General Report KPI card is clicked.
/// Any metric reuses this one panel: it asks <see cref="KPIManager"/> for the metric's detail
/// payload (host builds locally, client round-trips) and spawns one <see cref="KpiDetailRowUI"/>
/// per entry.
/// </summary>
public class KpiDetailPanelUI : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("Object toggled on/off when the panel opens/closes. Defaults to this GameObject.")]
    [SerializeField] private GameObject root;

    [Header("Header")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private TMP_Text explanationText;

    [Header("List")]
    [SerializeField] private Transform entriesContainer;
    [SerializeField] private GameObject rowPrefab;
    [Tooltip("Optional text shown when there are no entries to display.")]
    [SerializeField] private TMP_Text emptyText;

    [Header("Controls")]
    [SerializeField] private Button closeButton;

    private readonly List<GameObject> _spawnedRows = new List<GameObject>();
    private bool _subscribed;

    private void Awake()
    {
        if (root == null) root = gameObject;
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
        root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(Hide);
        SetSubscription(false);
    }

    /// <summary>Opens the panel and fetches the detail payload for the given metric.</summary>
    public void Show(KpiMetric metric)
    {
        root.SetActive(true);

        if (titleText != null) titleText.text = PrettyName(metric);
        if (valueText != null) valueText.text = "";
        if (explanationText != null) explanationText.text = "Loading…";
        ClearRows();
        if (emptyText != null) emptyText.gameObject.SetActive(false);

        SetSubscription(true);

        if (KPIManager.Instance != null)
        {
            KPIManager.Instance.RequestKpiDetail(metric);
        }
        else if (explanationText != null)
        {
            explanationText.text = "KPI data unavailable.";
        }
    }

    public void Hide()
    {
        SetSubscription(false);
        ClearRows();
        root.SetActive(false);
    }

    private void SetSubscription(bool on)
    {
        if (on == _subscribed || KPIManager.Instance == null) { _subscribed = on && KPIManager.Instance != null; return; }

        if (on) KPIManager.Instance.OnKpiDetailReady += OnDetailReady;
        else KPIManager.Instance.OnKpiDetailReady -= OnDetailReady;
        _subscribed = on;
    }

    private void OnDetailReady(KpiDetailData data)
    {
        if (valueText != null) valueText.text = data.headerValue;
        if (explanationText != null) explanationText.text = data.explanation;

        ClearRows();

        int count = data.entries != null ? data.entries.Count : 0;
        if (emptyText != null) emptyText.gameObject.SetActive(count == 0);

        if (count == 0 || rowPrefab == null || entriesContainer == null) return;

        foreach (var entry in data.entries)
        {
            GameObject rowObj = Instantiate(rowPrefab, entriesContainer);
            if (rowObj.TryGetComponent<KpiDetailRowUI>(out var row)) row.Setup(entry);
            _spawnedRows.Add(rowObj);
        }
    }

    private void ClearRows()
    {
        for (int i = 0; i < _spawnedRows.Count; i++)
            if (_spawnedRows[i] != null) Destroy(_spawnedRows[i]);
        _spawnedRows.Clear();
    }

    // Turns "CustomerSatisfaction" into "Customer Satisfaction".
    private static string PrettyName(KpiMetric metric)
    {
        string s = metric.ToString();
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
