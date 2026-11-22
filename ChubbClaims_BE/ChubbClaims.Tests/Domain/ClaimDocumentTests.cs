using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Events;
using ChubbClaims.Domain.Exceptions;
using ChubbClaims.Domain.Rules;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Domain;

/// <summary>Who may attach photos and documents, when, and what is recorded.</summary>
public class ClaimDocumentTests
{
    [Fact]
    public void The_claimant_attaches_a_photo_and_the_assigned_officer_is_named_on_the_event()
    {
        var claim = InStatus(ClaimStatus.UnderReview);
        var photo = Photo();

        var document = claim.AttachDocument(Claimant, photo, Now);

        claim.Documents.ShouldHaveSingleItem().ShouldBeSameAs(document);
        document.FileName.ShouldBe("rear-bumper.jpg");
        document.StorageKey.ShouldBe(photo.StorageKey);
        document.Sha256.ShouldBe(photo.Sha256);
        document.UploadedByRole.ShouldBe(UserRole.Claimant);
        claim.Status.ShouldBe(ClaimStatus.UnderReview);
        var attached = claim.DomainEvents[^1].ShouldBeOfType<DocumentAttached>();
        attached.DocumentId.ShouldBe(document.Id);
        attached.UploadedByRole.ShouldBe(UserRole.Claimant);
        attached.AssignedOfficerId.ShouldBe(Officer.UserId);
    }

    [Fact]
    public void A_photo_can_be_attached_at_FNOL_time_before_anyone_picks_the_claim_up()
    {
        var claim = InStatus(ClaimStatus.Submitted);

        claim.AttachDocument(Claimant, Photo(), Now);

        claim.DomainEvents[^1].ShouldBeOfType<DocumentAttached>().AssignedOfficerId.ShouldBeNull();
    }

    [Fact]
    public void Any_staff_member_in_the_market_may_attach_not_only_the_assigned_officer()
    {
        var claim = InStatus(ClaimStatus.UnderReview);

        claim.AttachDocument(OtherOfficer, Photo("assessor-report.pdf", "application/pdf"), Now);
        claim.AttachDocument(Manager, Photo(), Now);

        claim.Documents.Select(d => d.UploadedByRole).ShouldBe([UserRole.ClaimsOfficer, UserRole.ClaimsManager]);
    }

    [Fact]
    public void Another_claimant_or_staff_from_another_market_cannot_attach()
    {
        var claim = InStatus(ClaimStatus.UnderReview);

        Should.Throw<ClaimAccessDeniedException>(() => claim.AttachDocument(OtherClaimant, Photo(), Now));
        Should.Throw<ClaimAccessDeniedException>(() => claim.AttachDocument(SgOfficer, Photo(), Now));
        claim.Documents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ClaimStatus.Settled)]
    [InlineData(ClaimStatus.Rejected)]
    public void A_closed_claim_takes_no_more_documents_and_says_why(ClaimStatus closed)
    {
        var claim = InStatus(closed);

        var ex = Should.Throw<InvalidClaimTransitionException>(() => claim.EnsureCanAttachDocument(Claimant));

        ex.Message.ShouldBe($"This claim is {closed.ToString().ToLowerInvariant()}: no more documents can be added.");
    }

    [Fact]
    public void At_most_twenty_documents_per_claim()
    {
        var claim = InStatus(ClaimStatus.UnderReview);
        for (var i = 0; i < ClaimDocumentRules.MaxPerClaim; i++)
            claim.AttachDocument(Claimant, Photo(), Now);

        var ex = Should.Throw<DomainValidationException>(() => claim.AttachDocument(Claimant, Photo(), Now));

        ex.Message.ShouldBe("A claim can have at most 20 documents.");
        claim.Documents.Count.ShouldBe(ClaimDocumentRules.MaxPerClaim);
    }

    [Theory]
    [InlineData("image/gif", 1_000)]
    [InlineData("image/svg+xml", 1_000)]
    [InlineData("image/jpeg", 0)]
    [InlineData("image/jpeg", ClaimDocumentRules.MaxBytes + 1)]
    public void Only_supported_types_within_the_size_limit(string contentType, long size)
    {
        var claim = InStatus(ClaimStatus.UnderReview);

        Should.Throw<DomainValidationException>(() => claim.AttachDocument(Claimant, Photo(contentType: contentType, sizeBytes: size), Now));
    }

    [Fact]
    public void The_file_name_is_required()
    {
        var claim = InStatus(ClaimStatus.UnderReview);

        Should.Throw<DomainValidationException>(() => claim.AttachDocument(Claimant, Photo(fileName: "  "), Now));
    }
}
