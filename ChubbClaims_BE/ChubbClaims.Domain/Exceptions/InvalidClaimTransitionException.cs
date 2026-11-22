using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Domain.Exceptions;

/// <summary>The action is not allowed from the claim's current status. Maps to 409 Conflict.</summary>
public sealed class InvalidClaimTransitionException(ClaimStatus from, ClaimAction action, string? message = null)
    : DomainException(message ?? $"Cannot {action} a claim that is {from}.")
{
    public ClaimStatus From { get; } = from;
    public ClaimAction Action { get; } = action;
}
