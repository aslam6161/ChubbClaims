namespace ChubbClaims.Domain.Enums;

/// <summary>The task-based actions on a claim. Each has its own rules, authorization and event.</summary>
public enum ClaimAction
{
    PickUp = 1,
    RequestInformation = 2,
    ProvideInformation = 3,
    AdjustReserve = 4,
    Approve = 5,
    Settle = 6,
    Reject = 7,
}
