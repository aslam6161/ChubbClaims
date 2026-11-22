using System.ComponentModel.DataAnnotations;
using ChubbClaims.Domain.Rules;
using ChubbClaims.Presentation.Middleware;
using ChubbClaims.Service.DTOs.Claims;
using ChubbClaims.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ChubbClaims.Presentation.Controllers;

/// <summary>
/// Photos and documents on a claim, for its claimant and for staff in its market (the service
/// checks; anyone else gets 404). Files never have a public URL: they are read through this API.
/// </summary>
[Route("api/claims/{id:guid}/documents")]
public sealed class ClaimDocumentsController(IClaimDocumentService documents) : BaseApiController
{
    /// <summary>The largest file plus room for the multipart envelope around it.</summary>
    private const long MaxRequestBytes = ClaimDocumentRules.MaxBytes + 64 * 1024;

    /// <summary>
    /// Attach a photo (JPEG, PNG, WebP) or a PDF, up to 10 MB, while the claim is open. The type is
    /// detected from the file's bytes, not its name.
    /// </summary>
    [HttpPost]
    [Tags("Claimant", "Staff")]
    [EndpointSummary("Attach photo or document")]
    [Consumes("multipart/form-data")]
    [RejectLargeRequests(MaxRequestBytes, "The file is too large: a document can be at most 10 MB.")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<ClaimDocumentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ClaimDocumentResponse>> Upload(Guid id, [FromForm] UploadDocumentForm form, CancellationToken ct)
    {
        await using var content = form.File.OpenReadStream();
        var created = await documents.UploadAsync(id, new DocumentUpload(content, form.File.FileName, form.File.Length), ct);
        return CreatedAtAction(nameof(GetContent), new { id, documentId = created.Id }, created);
    }

    /// <summary>
    /// The file itself. Served inline with its detected type, <c>nosniff</c>, and privately cached:
    /// a stored document never changes.
    /// </summary>
    [HttpGet("{documentId:guid}/content")]
    [Tags("Claimant", "Staff")]
    [EndpointSummary("Read photo or document")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp", "application/pdf")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetContent(Guid id, Guid documentId, CancellationToken ct)
    {
        var file = await documents.OpenAsync(id, documentId, ct);

        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(file.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(file.Content, file.ContentType);
    }
}

public sealed class UploadDocumentForm
{
    /// <summary>The photo or PDF (form field "file").</summary>
    [Required, FromForm(Name = "file")] public IFormFile File { get; set; } = null!;
}
