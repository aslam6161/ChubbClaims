namespace ChubbClaims.Domain.Exceptions;

/// <summary>
/// The actor may not perform this action on this claim (not the assigned officer, wrong market,
/// not the claim owner). Maps to 403. The aggregate enforces this so no caller can bypass it.
/// </summary>
public sealed class ClaimAccessDeniedException(string message) : DomainException(message);
