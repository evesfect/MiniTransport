using System;
using System.Collections.Generic;
using UnityEngine;

public enum EmployeeRole
{
    Driver,
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
}

[Serializable]
public class EmployeeContainer
{
    public List<EmployeeData> Employees;
    public List<EmployeeData> Candidates;
}