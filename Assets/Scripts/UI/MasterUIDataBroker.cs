using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class MasterUIDataBroker : MonoBehaviour, IUIDataProvider
{
    [SerializeField] private UIDataCache dataCache;
    private Dictionary<UIDataType, int> interestCounts = new Dictionary<UIDataType, int>();

    public void RegisterInterest(UIDataType mask)
    {
        if ((mask & UIDataType.CompanyStats) != 0) HandleInterestAdded(UIDataType.CompanyStats);
        if ((mask & UIDataType.FleetStats) != 0) HandleInterestAdded(UIDataType.FleetStats);
    }

    public void UnregisterInterest(UIDataType mask)
    {
        if ((mask & UIDataType.CompanyStats) != 0) HandleInterestRemoved(UIDataType.CompanyStats);
        if ((mask & UIDataType.FleetStats) != 0) HandleInterestRemoved(UIDataType.FleetStats);
    }

    private void HandleInterestAdded(UIDataType type)
    {
        if (!interestCounts.ContainsKey(type))
        {
            interestCounts[type] = 0;
        }

        // Add the new UI window to the count
        interestCounts[type]++;

        // If this is the FIRST UI element to ask for this data, subscribe to the live network events
        if (interestCounts[type] == 1)
        {
            StartProvidingData(type);
        }
        else
        {
            // If we are already listening, just push the current state to the new window
            PushCurrentState(type);
        }
    }

    private void HandleInterestRemoved(UIDataType type)
    {
        if (interestCounts.ContainsKey(type))
        {
            interestCounts[type]--;
            if (interestCounts[type] <= 0)
            {
                interestCounts[type] = 0;
                StopProvidingData(type);
            }
        }
    }

    private void StartProvidingData(UIDataType type)
    {
        if (type == UIDataType.CompanyStats && CompanyManager.Instance != null)
        {
            CompanyManager.Instance.OnBalanceChanged += OnCompanyBalanceUpdated;
            CompanyManager.Instance.OnTransactionAdded += OnCompanyTransaction;
            PushCurrentState(UIDataType.CompanyStats);
        }
        else if (type == UIDataType.FleetStats && FleetManager.Instance != null)
        {
            FleetManager.Instance.OnFleetUpdated += OnFleetUpdated;
            PushCurrentState(UIDataType.FleetStats);
        }
    }

    private void StopProvidingData(UIDataType type)
    {
        if (type == UIDataType.CompanyStats && CompanyManager.Instance != null)
        {
            CompanyManager.Instance.OnBalanceChanged -= OnCompanyBalanceUpdated;
            CompanyManager.Instance.OnTransactionAdded -= OnCompanyTransaction;
        }
        else if (type == UIDataType.FleetStats && FleetManager.Instance != null)
        {
            FleetManager.Instance.OnFleetUpdated -= OnFleetUpdated;
        }
    }

    // --- Event Listeners ---
    private void OnCompanyBalanceUpdated(float newBalance) => PushCurrentState(UIDataType.CompanyStats);
    private void OnCompanyTransaction(Transaction t) => PushCurrentState(UIDataType.CompanyStats);
    private void OnFleetUpdated() => PushCurrentState(UIDataType.FleetStats);

    // --- Core Logic ---
    private void PushCurrentState(UIDataType type)
    {
        if (type == UIDataType.CompanyStats && CompanyManager.Instance != null)
        {
            CompanyData realData = CompanyManager.Instance.GetCompanyData();
            if (realData != null)
            {
                dataCache.SetCompanyData(new CompanyStatsData { 
                    currentBalance = realData.CurrentBalance,
                    totalTransactions = realData.History.Count
                });
            }
        }
        else if (type == UIDataType.FleetStats && FleetManager.Instance != null)
        {
            var currentBuses = FleetManager.Instance.allBuses;
            
            dataCache.SetFleetData(new FleetStatsData {
                totalBuses = currentBuses.Count,
                lowDurabilityBuses = currentBuses.Count(b => b.Durability < 50f) 
            });
        }
    }
}