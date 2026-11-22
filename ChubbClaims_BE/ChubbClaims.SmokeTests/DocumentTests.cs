using System.Text;
using ChubbClaims.SmokeTests.Infrastructure;
using static ChubbClaims.SmokeTests.Infrastructure.Users;

namespace ChubbClaims.SmokeTests;

/// <summary>Photos and documents: upload and read back, refused files, who may see them, and the notification.</summary>
public class DocumentTests
{
    private static string Content(Guid claimId, Guid documentId) => $"/api/claims/{claimId}/documents/{documentId}/content";

    [SmokeFact]
    public async Task A_photo_is_stored_with_the_type_detected_from_its_bytes_and_read_back_unchanged()
    {
        var claim = await Claims.Submit();
        var photo = Files.Png();

        var uploaded = await Api.Upload(claim.Id, ClaimantMy, "front bumper.png", photo);
        uploaded.Status.ShouldBe(HttpStatusCode.Created, uploaded.ToString());
        var doc = uploaded.Json;
        doc.Str("contentType").ShouldBe("image/png");
        doc.Str("fileName").ShouldBe("front bumper.png");
        doc.Int("sizeBytes").ShouldBe(photo.Length);
        doc.Str("uploadedByRole").ShouldBe("Claimant");
        uploaded.Response.Headers.Location!.ToString().ShouldContain($"/documents/{doc.Id()}/content");

        var read = await Api.Get(Content(claim.Id, doc.Id()), ClaimantMy);
        read.Status.ShouldBe(HttpStatusCode.OK);
        read.Bytes.ShouldBe(photo, "the same bytes");
        read.Response.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        read.Response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("inline");
        read.Response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        read.Response.Headers.CacheControl!.Private.ShouldBeTrue();

        var pdf = await Api.Upload(claim.Id, ClaimantMy, "police-report.jpg", Files.Pdf);
        pdf.Status.ShouldBe(HttpStatusCode.Created);
        pdf.Json.Str("contentType").ShouldBe("application/pdf", "the bytes decide, not the name");

        await Claims.Close(claim.Id);
    }

    public static TheoryData<string, string, byte[]> RefusedFiles => new()
    {
        { "an executable renamed .jpg", "holiday.jpg", [0x4D, 0x5A, 0x90, 0x00, .. Encoding.ASCII.GetBytes(" this is a program")] },
        { "an SVG, which can carry script", "logo.svg", Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>") },
        { "an empty file", "empty.jpg", [] },
    };

    [SmokeTheory]
    [MemberData(nameof(RefusedFiles))]
    public async Task A_file_that_is_not_a_photo_or_PDF_is_refused(string what, string fileName, byte[] content)
    {
        var claim = await Claims.Submit();
        var response = await Api.Upload(claim.Id, ClaimantMy, fileName, content);
        if (content.Length == 0) ((int)response.Status).ShouldBeOneOf([400, 422], $"{what}: {response}");
        else
        {
            response.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, $"{what}: {response}");
            response.Detail.ShouldNotBeNull().ShouldContain("JPEG, PNG or WebP");
        }
        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task An_11_MB_upload_gets_413_from_its_declared_size_before_the_body_is_sent()
    {
        var claim = await Claims.Submit();
        var (status, body) = await Api.SendHeadersOnly("POST", $"/api/claims/{claim.Id}/documents", ClaimantMy,
            new Dictionary<string, string>
            {
                ["Content-Type"] = "multipart/form-data; boundary=x",
                ["Content-Length"] = (11 * 1024 * 1024).ToString(),
            });
        status.ShouldBe(413);
        body.ShouldNotBeNull().Str("detail").ShouldContain("at most 10 MB");
        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task An_upload_without_the_file_field_is_a_400_validation_problem()
    {
        var claim = await Claims.Submit();
        var response = await Api.Upload(claim.Id, ClaimantMy, "x.png", Files.Png(), field: "wrong");
        response.Status.ShouldBe(HttpStatusCode.BadRequest);
        response.Json["errors"].ShouldNotBeNull();
        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task Only_people_who_can_see_the_claim_can_see_or_add_its_files_and_others_get_404()
    {
        var claim = await Claims.Submit();
        var photo = Files.Png();
        var doc = (await Api.Upload(claim.Id, ClaimantMy, "front.png", photo)).Json.Id();

        (await Api.Get(Content(claim.Id, doc), ClaimantSg)).Status.ShouldBe(HttpStatusCode.NotFound, "another claimant reading");
        (await Api.Upload(claim.Id, ClaimantSg, "mine.png", photo)).Status.ShouldBe(HttpStatusCode.NotFound, "another claimant attaching");
        (await Api.Get(Content(claim.Id, doc), OfficerSg)).Status.ShouldBe(HttpStatusCode.NotFound, "an officer in another market");
        (await Api.Get(Content(claim.Id, Guid.NewGuid()), ClaimantMy)).Status.ShouldBe(HttpStatusCode.NotFound, "an unknown document");

        var officerRead = await Api.Get(Content(claim.Id, doc), OfficerMy);
        officerRead.Status.ShouldBe(HttpStatusCode.OK, "an officer in the claim's market");
        officerRead.Bytes.ShouldBe(photo);

        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task Staff_can_attach_too_and_the_claimant_is_told()
    {
        var claim = await Claims.Submit();
        await Api.Upload(claim.Id, ClaimantMy, "front bumper.png", Files.Png());
        await Api.Upload(claim.Id, ClaimantMy, "police-report.pdf", Files.Pdf);

        var staffUpload = await Api.Upload(claim.Id, OfficerMy, "assessor-note.pdf", Files.Pdf);
        staffUpload.Status.ShouldBe(HttpStatusCode.Created);
        staffUpload.Json.Str("uploadedByRole").ShouldBe("ClaimsOfficer");

        (await Claims.Detail(claim.Id, ClaimantMy)).List("documents").Select(d => d.Str("fileName"))
            .ShouldBe(new[] { "front bumper.png", "police-report.pdf", "assessor-note.pdf" }, "oldest first");

        var message = await Api.WaitFor(async () => (await Api.Get("/api/notifications", ClaimantMy)).Json.List()
            .Where(n => n.Id("claimId") == claim.Id && n.Str("message").Contains("added a document"))
            .Select(n => n.Str("message")).FirstOrDefault());
        message.ShouldBe($"The claims team added a document to your claim {claim.Number}: assessor-note.pdf");

        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task A_closed_claim_takes_no_more_files()
    {
        var claim = await Claims.Submit();
        await Claims.Close(claim.Id);

        var response = await Api.Upload(claim.Id, ClaimantMy, "late.png", Files.Png());
        response.Status.ShouldBe(HttpStatusCode.Conflict);
        response.Detail.ShouldNotBeNull().ShouldContain("no more documents");
    }
}
