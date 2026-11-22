using System.Net;
using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Interfaces;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;

namespace ChubbClaims.Service.Storage;

/// <summary>
/// Documents in Cloudinary, uploaded as <b>private</b> assets: there is no public delivery URL. To
/// read one, the API asks for a download link signed with the secret that expires in two minutes,
/// fetches the bytes and streams them to the caller it has already authorized. The only class that
/// references the Cloudinary SDK.
/// </summary>
/// <remarks>
/// PDFs are stored as Cloudinary "image" assets too (Cloudinary treats a PDF as a multi-page
/// image), so one resource type covers every document.
/// </remarks>
public sealed class CloudinaryDocumentStorage : IDocumentStorage, IDisposable
{
    private const string PrivateType = "private";
    private static readonly TimeSpan DownloadLinkLifetime = TimeSpan.FromMinutes(2);

    private readonly Cloudinary cloudinary;
    private readonly CloudinarySettings settings;
    private readonly TimeProvider clock;
    private readonly ILogger<CloudinaryDocumentStorage> logger;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public CloudinaryDocumentStorage(CloudinarySettings settings, TimeProvider clock, ILogger<CloudinaryDocumentStorage> logger)
    {
        this.settings = settings;
        this.clock = clock;
        this.logger = logger;
        cloudinary = new Cloudinary(new Account(settings.CloudName, settings.ApiKey, settings.ApiSecret));
        cloudinary.Api.Secure = true;
    }

    public string Name => $"Cloudinary (private, folder {settings.Folder})";

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        var (publicId, format) = Locate(key);
        var parameters = new ImageUploadParams
        {
            File = new FileDescription($"{Path.GetFileNameWithoutExtension(key)}.{format}", content),
            PublicId = publicId,
            Type = PrivateType,
            Overwrite = false, // evidence is never replaced
        };

        ImageUploadResult result;
        try
        {
            result = await cloudinary.UploadAsync(parameters, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DocumentStorageException("Document storage could not be reached.", ex);
        }

        if (result.Error is not null)
        {
            // The vendor's message is logged, never returned to the user.
            logger.LogWarning("Cloudinary refused {Key}: {Error}", key, result.Error.Message);
            throw new DocumentStorageException("The document could not be stored.");
        }
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var (publicId, format) = Locate(key);
        var expiresAt = clock.GetUtcNow().Add(DownloadLinkLifetime).ToUnixTimeSeconds();
        var url = cloudinary.DownloadPrivate(publicId, attachment: false, format: format, type: PrivateType, expiresAt: expiresAt);

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new DocumentStorageException("Document storage could not be reached.", ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Cloudinary download of {Key} failed with {Status}", key, (int)response.StatusCode);
            response.Dispose();
            throw new DocumentStorageException("The document could not be read.");
        }

        // Disposing the returned stream releases the response.
        return await response.Content.ReadAsStreamAsync(ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        var (publicId, _) = Locate(key);
        var result = await cloudinary.DestroyAsync(new DeletionParams(publicId) { Type = PrivateType, ResourceType = ResourceType.Image });
        if (result.Error is not null && result.StatusCode != HttpStatusCode.NotFound)
            logger.LogWarning("Cloudinary could not delete {Key}: {Error}", key, result.Error.Message);
    }

    public void Dispose() => http.Dispose();

    /// <summary>Key <c>claims/a/b.jpg</c> → public id <c>{folder}/claims/a/b</c>, format <c>jpg</c>.</summary>
    private (string PublicId, string Format) Locate(string key)
    {
        if (!DocumentKeys.IsValid(key))
            throw new ArgumentException($"Not a document key: {key}", nameof(key));
        var dot = key.LastIndexOf('.');
        return ($"{settings.Folder.TrimEnd('/')}/{key[..dot]}", key[(dot + 1)..]);
    }
}
