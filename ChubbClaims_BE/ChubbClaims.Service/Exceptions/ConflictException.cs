namespace ChubbClaims.Service.Exceptions;

/// <summary>
/// The request conflicts with the current state of the resource
/// (e.g. another officer changed the claim first).
/// </summary>
public sealed class ConflictException(string message) : Exception(message);
