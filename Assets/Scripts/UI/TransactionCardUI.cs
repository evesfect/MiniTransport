using TMPro;
using UnityEngine;

public class TransactionCardUI : MonoBehaviour
{
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI categoryText;
    public TextMeshProUGUI amountText;
    public TextMeshProUGUI timestampText;
    public TextMeshProUGUI countText;

    public void Setup(Transaction tx)
    {
        descriptionText.text = tx.Description;
        categoryText.text = tx.Category.ToString();
        timestampText.text = $"Day {tx.Timestamp}";
        
        // Only show count if it aggregated multiple transactions
        countText.text = tx.Count > 1 ? $"x{tx.Count}" : "";

        if (tx.Amount >= 0)
        {
            amountText.text = $"+${tx.Amount:F2}";
            amountText.color = new Color(0.2f, 0.8f, 0.2f); // Green
        }
        else
        {
            amountText.text = $"-${Mathf.Abs(tx.Amount):F2}";
            amountText.color = new Color(0.9f, 0.2f, 0.2f); // Red
        }
    }
}