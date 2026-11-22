namespace ChubbClaims.Service.Interfaces;

/// <summary>
/// Where claim photos and documents are kept: Cloudinary when it is configured, a local folder
/// otherwise. The use cases talk to this port, never to a vendor SDK, so the evidence rules are
/// tested without an account or a network.
/// </summary>
/// <remarks>
/// <para>
/// <b>Claim evidence is personal data, so nothing in storage is public.</b> There are no public
/// URLs: the API checks that the caller may see the claim and then streams the file. Signed,
/// expiring links straight from the CDN would save the API the bandwidth; that is the step to take
/// when volume needs it (docs/decisions.md).
/// </para>
/// <para>
/// Keys are chosen by the server (<c>claims/{claimId}/{random}.{ext}</c>), never from the
/// uploaded file name, so they can't collide, can't be guessed and can't escape their folder.
/// </para>
/// </remarks>
public interface IDocumentStorage
{
    /// <summary>For logs and the health of the setup, e.g. "Cloudinary" or "local disk".</summary>
    string Name { get; }

    /// <summary>Stores the bytes under <paramref name="key"/>. Never overwrites.</summary>
    /// <exception cref="Exceptions.DocumentStorageException">Storage refused or could not be reached.</exception>
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct);

    /// <summary>The stored bytes, or null if nothing is stored under the key.</summary>
    /// <exception cref="Exceptions.DocumentStorageException">Storage could not be reached.</exception>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);

    /// <summary>Removes the file. Succeeds if it is already gone (safe to retry).</summary>
    Task DeleteAsync(string key, CancellationToken ct);
}
