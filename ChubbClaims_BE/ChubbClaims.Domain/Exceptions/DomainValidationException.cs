namespace ChubbClaims.Domain.Exceptions;

/// <summary>Input breaks a business rule (e.g. negative reserve, expired policy). Maps to 422.</summary>
public sealed class DomainValidationException(string message) : DomainException(message);
