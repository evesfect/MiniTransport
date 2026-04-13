using UnityEngine;
using Unity.Netcode;

public class CompanyDebugBalanceSimulator : MonoBehaviour
{
    [Header("Simulation Settings")]
    [Tooltip("How often a transaction occurs (in seconds)")]
    public float updateInterval = 0.5f;
    
    public float minAmount = 10f;
    public float maxAmount = 500f;

    private float _timer = 0f;

    private void Update()
    {
        // Wait until the CompanyManager is fully loaded
        if (CompanyManager.Instance == null) return;

        // Only run the simulation on the Server/Host so we don't spam client RPCs
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;

        _timer += Time.deltaTime;
        if (_timer >= updateInterval)
        {
            _timer = 0f;
            SimulateTransaction();
        }
    }

    private void SimulateTransaction()
    {
        // Generate a random amount
        float amount = Random.Range(minAmount, maxAmount);
        
        // 50/50 chance to either gain or lose money
        bool isIncome = Random.value > 0.5f;

        if (isIncome)
        {
            CompanyManager.Instance.AddIncome(amount, TransactionCategory.TicketRevenue, "Simulated Passenger Fare");
        }
        else
        {
            CompanyManager.Instance.ProcessPassiveExpense(amount, TransactionCategory.Maintenance, "Simulated Random Repair");
        }
    }
}