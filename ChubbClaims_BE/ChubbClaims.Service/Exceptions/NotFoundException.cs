namespace ChubbClaims.Service.Exceptions;

/// <summary>
/// The requested resource does not exist, or the caller is not allowed to know it exists
/// (a claimant asking for another claimant's claim gets this, not a 403).
/// </summary>
public sealed class NotFoundException(string message) : Exception(message);
