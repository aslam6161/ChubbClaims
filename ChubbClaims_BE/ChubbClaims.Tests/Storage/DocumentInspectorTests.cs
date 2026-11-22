using System.Text;
using ChubbClaims.Service.Helper;
using ChubbClaims.Service.Storage;
using Shouldly;

namespace ChubbClaims.Tests.Storage;

/// <summary>The file type comes from the bytes; names are cleaned; keys can't escape their folder.</summary>
public class DocumentInspectorTests
{
    public static TheoryData<string, byte[]> Accepted => new()
    {
        { "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1] },
        { "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D] },
        { "image/webp", [.. "RIFF"u8, 0x24, 0x08, 0, 0, .. "WEBP"u8] },
        { "application/pdf", [.. "%PDF-1.7\n%"u8] },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void Detects_supported_types_from_their_first_bytes(string expected, byte[] header) =>
        DocumentInspector.DetectContentType(header).ShouldBe(expected);

    [Theory]
    [InlineData("GIF89a......")] // GIF: not on the list
    [InlineData("<svg xmlns=")] // SVG can carry script
    [InlineData("<html><scrip")]
    [InlineData("MZ\u0090\0\u0003\0\0\0")] // a Windows executable renamed to .jpg
    [InlineData("\0\0\0\u0018ftypheic")] // HEIC: browsers can't show it
    [InlineData("RIFF\0\0\0\0WAVE")] // RIFF, but audio
    [InlineData("")]
    public void Refuses_everything_else(string header) =>
        DocumentInspector.DetectContentType(Encoding.Latin1.GetBytes(header)).ShouldBeNull();

    [Theory]
    [InlineData("photo.jpg", "photo.jpg")]
    [InlineData(@"C:\Users\me\Pictures\bumper.jpg", "bumper.jpg")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("quote\"<script>.pdf", "quotescript.pdf")]
    [InlineData("line\r\nbreak.png", "linebreak.png")]
    [InlineData("   ", "document")]
    [InlineData(null, "document")]
    [InlineData("...", "document")]
    public void Cleans_file_names_for_display(string? raw, string expected) =>
        DocumentInspector.CleanFileName(raw).ShouldBe(expected);

    [Fact]
    public void A_long_name_is_shortened_but_keeps_its_extension()
    {
        var cleaned = DocumentInspector.CleanFileName(new string('x', 300) + ".jpeg");

        cleaned.Length.ShouldBe(120);
        cleaned.ShouldEndWith(".jpeg");
    }

    [Fact]
    public void Keys_are_made_from_the_claim_and_the_detected_type_only()
    {
        var claimId = Guid.NewGuid();

        var key = DocumentKeys.For(claimId, "application/pdf");

        key.ShouldStartWith($"claims/{claimId:N}/");
        key.ShouldEndWith(".pdf");
        DocumentKeys.IsValid(key).ShouldBeTrue();
        DocumentKeys.For(claimId, "application/pdf").ShouldNotBe(key);
    }

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("claims/../../secrets.json")]
    [InlineData("claims/0123456789abcdef0123456789abcdef/../x.jpg")]
    [InlineData("claims/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef.exe")]
    [InlineData("/claims/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef.jpg")]
    public void Anything_else_is_not_a_key(string key) => DocumentKeys.IsValid(key).ShouldBeFalse();
}
