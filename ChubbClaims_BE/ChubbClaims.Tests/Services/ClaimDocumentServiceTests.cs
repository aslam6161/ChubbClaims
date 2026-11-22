using System.Security.Cryptography;
using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Exceptions;
using ChubbClaims.Domain.Rules;
using ChubbClaims.Domain.ValueObjects;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.DTOs.Claims;
using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Interfaces;
using ChubbClaims.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Services;

/// <summary>The upload order (check → store → attach → save, and clean up on failure) and who may read.</summary>
public class ClaimDocumentServiceTests
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, .. "JFIF"u8, 0, 1, 2, 3];
    private static readonly byte[] PdfBytes = [.. "%PDF-1.7\n1 0 obj << >> endobj"u8];

    private readonly IClaimRepository _claims = Substitute.For<IClaimRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDocumentStorage _storage = Substitute.For<IDocumentStorage>();

    private ClaimDocumentService ServiceFor(Actor caller) => new(
        _claims, _unitOfWork, new TestCurrentUser(caller), _storage, new FixedTimeProvider(Now), NullLogger<ClaimDocumentService>.Instance);

    private Claim Existing(ClaimStatus status = ClaimStatus.UnderReview)
    {
        var claim = InStatus(status);
        _claims.GetAsync(claim.Id, Arg.Any<CancellationToken>()).Returns(claim);
        return claim;
    }

    private static DocumentUpload Upload(byte[] bytes, string? name = "bumper.jpg", long? declared = null) =>
        new(new MemoryStream(bytes), name, declared ?? bytes.Length);

    [Fact]
    public async Task Stores_the_file_under_a_server_key_then_attaches_it_with_the_detected_type_and_hash()
    {
        var claim = Existing();

        var response = await ServiceFor(Claimant).UploadAsync(claim.Id, Upload(PdfBytes, name: "police report.jpg"), CancellationToken.None);

        var document = claim.Documents.ShouldHaveSingleItem();
        document.ContentType.ShouldBe("application/pdf"); // the bytes win over the ".jpg" name
        document.FileName.ShouldBe("police report.jpg");
        document.StorageKey.ShouldMatch($"^claims/{claim.Id:N}/[0-9a-f]{{32}}\\.pdf$");
        document.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(PdfBytes)));
        document.SizeBytes.ShouldBe(PdfBytes.Length);
        response.Id.ShouldBe(document.Id);
        await _storage.Received(1).SaveAsync(document.StorageKey, Arg.Any<Stream>(), "application/pdf", Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_closed_claim_is_refused_before_anything_is_stored()
    {
        var claim = Existing(ClaimStatus.Settled);

        await Should.ThrowAsync<InvalidClaimTransitionException>(() =>
            ServiceFor(Claimant).UploadAsync(claim.Id, Upload(JpegBytes), CancellationToken.None));

        await _storage.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Someone_who_cannot_see_the_claim_gets_not_found_and_nothing_is_stored()
    {
        var claim = Existing();

        await Should.ThrowAsync<NotFoundException>(() =>
            ServiceFor(OtherClaimant).UploadAsync(claim.Id, Upload(JpegBytes), CancellationToken.None));
        await Should.ThrowAsync<NotFoundException>(() =>
            ServiceFor(SgOfficer).UploadAsync(claim.Id, Upload(JpegBytes), CancellationToken.None));

        await _storage.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task A_file_that_is_not_a_supported_type_is_refused_whatever_its_name()
    {
        var claim = Existing();

        var ex = await Should.ThrowAsync<DomainValidationException>(() =>
            ServiceFor(Claimant).UploadAsync(claim.Id, Upload([.. "MZ not a photo"u8], name: "photo.jpg"), CancellationToken.None));

        ex.Message.ShouldBe("Only JPEG, PNG or WebP photos and PDF documents can be attached.");
        await _storage.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Size_is_checked_while_reading_not_taken_from_the_request()
    {
        var claim = Existing();
        var tooBig = new byte[ClaimDocumentRules.MaxBytes + 1];
        JpegBytes.CopyTo(tooBig, 0);

        // The client declares a small file but sends more.
        await Should.ThrowAsync<DomainValidationException>(() =>
            ServiceFor(Claimant).UploadAsync(claim.Id, Upload(tooBig, declared: 100), CancellationToken.None));
        await Should.ThrowAsync<DomainValidationException>(() =>
            ServiceFor(Claimant).UploadAsync(claim.Id, Upload([]), CancellationToken.None));

        await _storage.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task If_saving_the_claim_fails_the_stored_file_is_removed_again()
    {
        var claim = Existing();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database down"));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            ServiceFor(Claimant).UploadAsync(claim.Id, Upload(JpegBytes), CancellationToken.None));

        var stored = _storage.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IDocumentStorage.SaveAsync));
        await _storage.Received(1).DeleteAsync((string)stored.GetArguments()[0]!, CancellationToken.None);
    }

    [Fact]
    public async Task The_claimant_and_staff_in_the_market_can_read_a_document_others_get_not_found()
    {
        var claim = Existing();
        var document = claim.AttachDocument(Claimant, Photo(), Now);
        _storage.OpenReadAsync(document.StorageKey, Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream(JpegBytes));

        (await ServiceFor(Claimant).OpenAsync(claim.Id, document.Id, CancellationToken.None)).ContentType.ShouldBe("image/jpeg");
        (await ServiceFor(Manager).OpenAsync(claim.Id, document.Id, CancellationToken.None)).FileName.ShouldBe("rear-bumper.jpg");
        await Should.ThrowAsync<NotFoundException>(() => ServiceFor(OtherClaimant).OpenAsync(claim.Id, document.Id, CancellationToken.None));
        await Should.ThrowAsync<NotFoundException>(() => ServiceFor(SgOfficer).OpenAsync(claim.Id, document.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_document_id_from_another_claim_or_missing_from_storage_is_not_found()
    {
        var claim = Existing();
        var document = claim.AttachDocument(Claimant, Photo(), Now);
        _storage.OpenReadAsync(document.StorageKey, Arg.Any<CancellationToken>()).Returns((Stream?)null);

        await Should.ThrowAsync<NotFoundException>(() => ServiceFor(Claimant).OpenAsync(claim.Id, Guid.NewGuid(), CancellationToken.None));
        await Should.ThrowAsync<NotFoundException>(() => ServiceFor(Claimant).OpenAsync(claim.Id, document.Id, CancellationToken.None));
    }
}
