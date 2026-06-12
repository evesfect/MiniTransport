using System;
using System.Collections.Generic;
using UnityEngine;

public enum EmployeeRole
{
    Mechanic
    
}

[Serializable]
public class EmployeeData
{
    public string EmployeeID;
    public string FullName;
    public EmployeeRole Role;

    [Range(0, 100)]
    public float SkillLevel; // 0 to 100

    [Header("Financials")]
    [Tooltip("Weekly wage paid to the employee.")]
    public float WeeklySalary;

    [Header("Assignment")]
    public string AssignedBusID;
    public string AssignedDepotID;
    [Header("Team Logistics")]
    public string AssignedTeamID;

    [Header("Training")]
    [Tooltip("Days of training still remaining. While > 0 the mechanic is away on a course and does not contribute to repair work.")]
    public int TrainingDaysRemaining;

    // True while the employee is away on a training course (counts down on each day change).
    public bool IsInTraining => TrainingDaysRemaining > 0;
}

[Serializable]
public class EmployeeContainer
{
    public List<EmployeeData> Employees;
    public List<EmployeeData> Candidates;

    // Recruitment campaign state, synced to clients so the HR banner is correct on all peers.
    public bool AdCampaignActive;
    public AdTier PendingAdTier;
}