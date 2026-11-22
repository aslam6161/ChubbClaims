namespace ChubbClaims.Service.Storage;

/// <summary>Bound from "DocumentStorage". Used when Cloudinary is not configured.</summary>
public sealed class DocumentStorageSettings
{
    public const string SectionName = "DocumentStorage";

    /// <summary>Folder for the local-disk fallback (a Docker volume in compose). Relative to the app.</summary>
    public string LocalPath { get; set; } = "data/documents";
}

/// <summary>
/// Bound from "Cloudinary". When all three credentials are set, documents go to Cloudinary;
/// otherwise the API uses local disk, so a fresh clone runs with no account.
/// </summary>
/// <remarks>
/// <b>The API secret never goes in appsettings.json.</b> Locally it comes from user-secrets, in
/// Docker from the git-ignored .env file. Anyone holding it can read and delete every document.
/// </remarks>
public sealed class CloudinarySettings
{
    public const string SectionName = "Cloudinary";

    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>Root folder for every file. Set per environment so a test clean-up can't touch real evidence.</summary>
    public string Folder { get; set; } = "chubbclaims-dev";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudName) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret);
}
