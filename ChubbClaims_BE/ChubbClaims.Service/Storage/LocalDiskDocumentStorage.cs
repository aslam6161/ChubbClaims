using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Interfaces;

namespace ChubbClaims.Service.Storage;

/// <summary>
/// Documents in a local folder: the fallback when Cloudinary isn't configured, so a fresh clone
/// (and the reviewer's <c>docker compose up</c>) can attach photos with no account. In Docker the
/// folder is a named volume, so files survive a container rebuild.
/// </summary>
/// <remarks>Not for more than one API instance: they would each have their own folder.</remarks>
public sealed class LocalDiskDocumentStorage : IDocumentStorage
{
    private readonly string root;

    public LocalDiskDocumentStorage(DocumentStorageSettings settings)
    {
        root = Path.GetFullPath(settings.LocalPath, AppContext.BaseDirectory);
        Directory.CreateDirectory(root);
    }

    public string Name => $"local disk ({root})";

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        var path = PathFor(key);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // CreateNew: never overwrite. A key is random, so a clash means something is wrong.
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await content.CopyToAsync(file, ct);
        }
        catch (IOException ex)
        {
            throw new DocumentStorageException("The document could not be stored.", ex);
        }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path); // already gone (or its folder) is fine
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        if (!DocumentKeys.IsValid(key))
            throw new ArgumentException($"Not a document key: {key}", nameof(key));
        return Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
    }
}
