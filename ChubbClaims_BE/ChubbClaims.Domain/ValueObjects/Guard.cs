using ChubbClaims.Domain.Exceptions;

namespace ChubbClaims.Domain.ValueObjects;

internal static class Guard
{
    /// <summary>Returns the trimmed text, or throws if it is missing.</summary>
    public static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainValidationException($"{field} is required.")
            : value.Trim();
}
