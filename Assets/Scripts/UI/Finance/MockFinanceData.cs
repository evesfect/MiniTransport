using System;
using System.Collections.Generic;
using UnityEngine;

public struct MockTransaction
{
    public int GameDay;
    public float Amount;
    public TransactionCategory Category;
}

public struct MockApprovalRequest
{
    public string Department;
    public string Description;
    public float Cost;
}

public static class MockDataGenerator
{
    public static List<MockTransaction> GenerateMockTransactions()
    {
        List<MockTransaction> transactions = new List<MockTransaction>();
        System.Random rand = new System.Random();

        // Generate 30 days of fake data
        for (int day = 1; day <= 30; day++)
        {
            // Fake Revenue
            transactions.Add(new MockTransaction { GameDay = day, Amount = rand.Next(5000, 8000), Category = TransactionCategory.TicketRevenue });
            
            // Fake Costs
            transactions.Add(new MockTransaction { GameDay = day, Amount = -rand.Next(1000, 2000), Category = TransactionCategory.Fuel });
            transactions.Add(new MockTransaction { GameDay = day, Amount = -rand.Next(2000, 3000), Category = TransactionCategory.StaffSalary });
            
            // Random purchases
            if (day % 5 == 0)
                transactions.Add(new MockTransaction { GameDay = day, Amount = -rand.Next(500, 1500), Category = TransactionCategory.PartPurchase });
        }
        return transactions;
    }

    public static List<MockApprovalRequest> GenerateMockRequests()
    {
        return new List<MockApprovalRequest>
        {
            new MockApprovalRequest { Department = "Transportation", Description = "Buy new City Bus", Cost = 55000f },
            new MockApprovalRequest { Department = "Maintenance", Description = "Upgrade Depot Tools", Cost = 12000f },
            new MockApprovalRequest { Department = "HR", Description = "Company Retreat Bonus", Cost = 5000f }
        };
    }
}