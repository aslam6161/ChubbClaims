using System.Security.Cryptography;
using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Exceptions;
using ChubbClaims.Domain.Rules;
using ChubbClaims.Domain.ValueObjects;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.DTOs.Claims;
using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Helper;
using ChubbClaims.Service.Interfaces;
using ChubbClaims.Service.Storage;
using Microsoft.Extensions.Logging;

namespace ChubbClaims.Service.Services;

/// <summary>
/// Photos and documents on a claim. Upload order matters, because storage and the database can't
/// share a transaction:
/// 1. check the caller may attach to this claim now (before any bytes are stored),
/// 2. read the file, detect its real type from the bytes, hash it,
/// 3. store it under a key the server chooses,
/// 4. attach it to the claim and save (the DocumentAttached event goes out through the outbox),
/// 5. if that save fails, delete the stored file again, so no orphan is left behind.
/// </summary>
public sealed class ClaimDocumentService(
    IClaimRepository claims,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDocumentStorage storage,
    TimeProvider clock,
    ILogger<ClaimDocumentService> logger) : IClaimDocumentService
{
    private static readonly string TooLarge = $"A document can be at most {ClaimDocumentRules.MaxBytes / (1024 * 1024)} MB.";

    public async Task<ClaimDocumentResponse> UploadAsync(Guid claimId, DocumentUpload upload, CancellationToken ct)
    {
        var claim = await claims.GetVisibleAsync(currentUser, claimId, ct);
        claim.EnsureCanAttachDocument(currentUser.Actor);

        if (upload.Length > ClaimDocumentRules.MaxBytes) throw new DomainValidationException(TooLarge);
        await using var buffer = await ReadAtMostAsync(upload.Content, ClaimDocumentRules.MaxBytes, ct);
        if (buffer.Length == 0) throw new DomainValidationException("The file is empty.");

        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        var contentType = DocumentInspector.DetectContentType(bytes[..Math.Min(bytes.Length, DocumentInspector.HeaderBytes)])
            ?? throw new DomainValidationException("Only JPEG, PNG or WebP photos and PDF documents can be attached.");
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var file = new DocumentFile(
            DocumentKeys.For(claim.Id, contentType), DocumentInspector.CleanFileName(upload.FileName), contentType, buffer.Length, sha256);

        buffer.Position = 0;
        await storage.SaveAsync(file.StorageKey, buffer, contentType, ct);

        ClaimDocument document;
        try
        {
            document = claim.AttachDocument(currentUser.Actor, file, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await DeleteQuietlyAsync(file.StorageKey);
            throw;
        }

        logger.LogInformation("Claim {ClaimNumber}: {ContentType} document {DocumentId} ({SizeBytes} bytes) attached by {ActorRole} {ActorId}",
            claim.ClaimNumber, contentType, document.Id, file.SizeBytes, currentUser.Role, currentUser.UserId);
        return document.ToResponse();
    }

    public async Task<DocumentContent> OpenAsync(Guid claimId, Guid documentId, CancellationToken ct)
    {
        var claim = await claims.GetVisibleAsync(currentUser, claimId, ct);
        var document = claim.Documents.SingleOrDefault(d => d.Id == documentId)
            ?? throw new NotFoundException($"Document {documentId} was not found.");

        var content = await storage.OpenReadAsync(document.StorageKey, ct);
        if (content is null)
        {
            // The row says it exists but storage doesn't have it: worth an alert, not a 500.
            logger.LogError("Claim {ClaimNumber}: document {DocumentId} is missing from {Storage}",
                claim.ClaimNumber, document.Id, storage.Name);
            throw new NotFoundException($"Document {documentId} was not found.");
        }

        return new DocumentContent(content, document.ContentType, document.FileName);
    }

    /// <summary>
    /// Copies the upload into memory, refusing it as soon as it passes <paramref name="limit"/>:
    /// the declared length is the client's word, so it is checked again while reading.
    /// </summary>
    private static async Task<MemoryStream> ReadAtMostAsync(Stream source, long limit, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                await buffer.DisposeAsync();
                throw new DomainValidationException(TooLarge);
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer;
    }

    private async Task DeleteQuietlyAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None); // even if the request was cancelled
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove orphaned document {Key} from {Storage}", key, storage.Name);
        }
    }
}
