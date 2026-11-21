namespace ChubbClaims.Service.Exceptions;

/// <summary>
/// The caller is authenticated and may see the resource, but may not perform this action on it
/// (e.g. an officer acting on a claim assigned to someone else).
/// </summary>
public sealed class ForbiddenException(string message) : Exception(message);
