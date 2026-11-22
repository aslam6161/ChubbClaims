namespace ChubbClaims.Service.DTOs;

/// <summary>Paging limits for list endpoints (the API rejects anything outside them with 400).</summary>
public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;

    /// <summary>Keeps (page - 1) * pageSize well inside an int, so a silly page number is a 400, not a 500.</summary>
    public const int MaxPage = 100_000;

    /// <summary>Trimmed search text, or null when there's nothing to search for.</summary>
    public static string? NormalizeSearch(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim();
}
