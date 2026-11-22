using System.Text.RegularExpressions;

namespace ChubbClaims.Service.Storage;

/// <summary>
/// Storage keys for claim documents: <c>claims/{claimId}/{random}.{ext}</c>. Made here from the
/// detected content type, so the uploaded file name never reaches storage.
/// </summary>
public static partial class DocumentKeys
{
    private static readonly IReadOnlyDictionary<string, string> ExtensionByContentType = new Dictionary<string, string>
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/webp"] = "webp",
        ["application/pdf"] = "pdf",
    };

    public static string For(Guid claimId, string contentType) =>
        $"claims/{claimId:N}/{Guid.NewGuid():N}.{ExtensionByContentType[contentType]}";

    /// <summary>
    /// True only for keys this class makes. Adapters check it before touching a path, so a bad key
    /// (say <c>../../appsettings.json</c>) can never reach the file system or another folder.
    /// </summary>
    public static bool IsValid(string key) => KeyPattern().IsMatch(key);

    [GeneratedRegex("^claims/[0-9a-f]{32}/[0-9a-f]{32}\\.(jpg|png|webp|pdf)$")]
    private static partial Regex KeyPattern();
}
