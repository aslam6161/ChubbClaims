namespace ChubbClaims.Domain.ValueObjects;

/// <summary>
/// A file already written to document storage, as the server measured it: the content type is
/// detected from the bytes, the size and SHA-256 are computed while storing, and the key is chosen
/// by the server. Nothing here is the uploader's word.
/// </summary>
/// <param name="StorageKey">Opaque key in document storage. Never shown to users, never a URL.</param>
/// <param name="FileName">The uploader's file name, cleaned, for display only.</param>
/// <param name="Sha256">Hex digest of the stored bytes, so the evidence can later be shown unchanged.</param>
public sealed record DocumentFile(string StorageKey, string FileName, string ContentType, long SizeBytes, string Sha256);
