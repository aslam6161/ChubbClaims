namespace ChubbClaims.Domain.Exceptions;

/// <summary>Base for business-rule failures raised by the domain model.</summary>
public abstract class DomainException(string message) : Exception(message);
