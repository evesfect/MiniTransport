using System;

public enum WorkItemStatus
{
    AwaitingTechnician,
    AwaitingParts,
    InRepair
}

[Serializable]
public class WorkItem
{
    public string WorkItemID;
    public string BusID;
    public BusPartType IssuePartType;
    public string AssignedTechnicianName;
    public string EstimatedCompletionLabel;
    public WorkItemStatus Status;
    public int Priority;

    public WorkItem(string busID, BusPartType issuePartType, WorkItemStatus status)
    {
        WorkItemID = Guid.NewGuid().ToString();
        BusID = busID;
        IssuePartType = issuePartType;
        Status = status;
        AssignedTechnicianName = string.Empty;
        EstimatedCompletionLabel = "Pending";
        Priority = int.MaxValue;
    }
}
