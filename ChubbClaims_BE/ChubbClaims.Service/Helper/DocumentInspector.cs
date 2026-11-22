using System.Text;
using ChubbClaims.Domain.Rules;

namespace ChubbClaims.Service.Helper;

/// <summary>
/// Decides what an uploaded file really is by reading its first bytes, and cleans its name for
/// display. The file name and the browser's content type are the uploader's word, so neither is
/// trusted: <c>invoice.pdf</c> that starts like an executable is refused.
/// </summary>
/// <remarks>
/// SVG and HTML are not accepted on purpose: they can carry script that runs when the file is
/// opened. HEIC (iPhone) is not accepted because browsers can't display it; iOS converts photos
/// to JPEG when they are picked in a browser.
/// </remarks>
public static class DocumentInspector
{
    /// <summary>Longest signature checked below.</summary>
    public const int HeaderBytes = 12;

    private static ReadOnlySpan<byte> Jpeg => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> Pdf => "%PDF-"u8;

    /// <summary>The detected content type, or null if it isn't one we accept.</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(Jpeg)) return "image/jpeg";
        if (header.StartsWith(Png)) return "image/png";
        if (header.StartsWith(Pdf)) return "application/pdf";
        // RIFF <size> WEBP: the four size bytes in the middle are skipped.
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }

    /// <summary>
    /// A file name safe to show and to put in a Content-Disposition header: no folders, no control
    /// characters, at most <see cref="ClaimDocumentRules.MaxFileNameLength"/> characters (keeping
    /// the extension). Falls back to "document" when nothing usable is left.
    /// </summary>
    public static string CleanFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..]; // a browser may send a full path
        var cleaned = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormC))
            if (!char.IsControl(c) && c is not ('"' or '<' or '>' or '|' or ':' or '*' or '?'))
                cleaned.Append(c);
        name = cleaned.ToString().Trim().Trim('.');
        if (name.Length == 0) return "document";
        if (name.Length <= ClaimDocumentRules.MaxFileNameLength) return name;

        var extension = Path.GetExtension(name);
        if (extension.Length > 10) extension = string.Empty;
        return name[..(ClaimDocumentRules.MaxFileNameLength - extension.Length)].TrimEnd() + extension;
    }
}
