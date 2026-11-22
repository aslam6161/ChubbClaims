namespace ChubbClaims.Service.DTOs.Claims;

/// <summary>An uploaded file on its way in. Length is what the request declared; it is checked again while reading.</summary>
public sealed record DocumentUpload(Stream Content, string? FileName, long Length);

/// <summary>A stored file on its way out, after the caller was allowed to see its claim.</summary>
public sealed record DocumentContent(Stream Content, string ContentType, string FileName);
