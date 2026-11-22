namespace ChubbClaims.Domain.Rules;

/// <summary>
/// Limits on photos and documents attached to a claim. Fixed rather than configured per market:
/// they protect the system (request size, storage), not a business decision that varies by market.
/// </summary>
public static class ClaimDocumentRules
{
    /// <summary>10 MB: a phone photo is 2–6 MB, a scanned police report well under that.</summary>
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>Enough evidence for one incident; stops a claim from becoming a file share.</summary>
    public const int MaxPerClaim = 20;

    /// <summary>Longest file name kept for display (the stored key never uses it).</summary>
    public const int MaxFileNameLength = 120;

    /// <summary>
    /// What can be attached, as detected from the file's bytes (never its name or the browser's
    /// claim). JPEG, PNG and WebP display in every browser; PDF covers police and repair reports.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedContentTypes =
        new HashSet<string>(StringComparer.Ordinal) { "image/jpeg", "image/png", "image/webp", "application/pdf" };
}
