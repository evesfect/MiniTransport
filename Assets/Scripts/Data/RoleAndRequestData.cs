using System;
using System.Collections.Generic;

public enum PlayerRole
{
    None,
    GeneralManager,
    MaintenanceManager,
    TransportManager,
    FinanceManager,
    HRManager
}

public enum RequestType
{
    HireMechanic,
    TrainMechanic,
    BuyParts,
    BuyBus,
    SellBus,
    TakeLoan
}

public enum RequestState
{
    Active,
    AwaitingGeneralManager, // Specifically for Finance -> GM forwards
    Completed,
    Rejected,
    Read // Safely archived/hidden
}

[Serializable]
public class GameRequest
{
    public string RequestID;
    public RequestType Type;
    
    public PlayerRole Requester;
    public PlayerRole CurrentTarget; // Who is currently responsible for this?

    public int TargetAmount;
    public int CurrentAmount;

    // Payload stores specific data as a JSON string (e.g., MinSkill level, or a list of BusIDs)
    public string Payload; 
    public string RejectReason;
    public RequestState State;

    // Helper to generate those nice notification sentences
    public string GetNotificationText()
    {
        if (State == RequestState.Rejected)
            return $"{CurrentTarget} rejected your request: {RejectReason}. (Completed {CurrentAmount}/{TargetAmount})";

        switch (Type)
        {
            case RequestType.HireMechanic:
                return $"{Requester} requests {TargetAmount} new Mechanics (Min Skill: {Payload}). Progress: {CurrentAmount}/{TargetAmount}";
            case RequestType.TrainMechanic:
                return $"{Requester} requests training for {TargetAmount} specific Mechanics. Progress: {CurrentAmount}/{TargetAmount}";
            case RequestType.BuyParts:
                return $"{Requester} requests purchasing {TargetAmount}x {Payload} from Vendors. Progress: {CurrentAmount}/{TargetAmount}";
            case RequestType.BuyBus:
                if (State == RequestState.AwaitingGeneralManager)
                    return $"Finance approved purchasing {TargetAmount} new buses. Awaiting General Manager final approval.";
                return $"{Requester} requests purchasing {TargetAmount} new buses.";
            case RequestType.SellBus:
                if (State == RequestState.AwaitingGeneralManager)
                    return $"Finance approved selling {TargetAmount} buses. Awaiting General Manager final approval.";
                return $"{Requester} requests selling {TargetAmount} specific buses.";
            case RequestType.TakeLoan:
                // Try to extract the formatted payload data
                string[] loanData = Payload?.Split(',');
                if (loanData != null && loanData.Length >= 4)
                    return $"{Requester} requests a {TargetAmount}$ loan (Interest: {loanData[0]}%, Duration: {loanData[1]} weeks).";
                return $"{Requester} requests a {TargetAmount}$ loan.";    
            default:
                return "Unknown Request.";
        }
    }
}

[Serializable]
public class RequestContainer
{
    public List<GameRequest> Requests;
}