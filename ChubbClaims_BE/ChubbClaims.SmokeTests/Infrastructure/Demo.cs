using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ChubbClaims.SmokeTests.Infrastructure;

/// <summary>Seeded demo users (dev-login), as "name" for <c>name@demo.local</c>.</summary>
public static class Users
{
    public const string ClaimantMy = "claimant.my";
    public const string ClaimantSg = "claimant.sg";
    public const string OfficerMy = "officer.my";
    public const string OfficerSg = "officer.sg";
    public const string ManagerMy = "manager.my";
}

/// <summary>A claim a test created for itself, so no test depends on the seed's exact state.</summary>
public sealed record NewClaim(Guid Id, string Number);

public static class Claims
{
    public static string Yesterday => DateOnly.FromDateTime(DateTime.Today.AddDays(-1)).ToString("yyyy-MM-dd");

    /// <summary>A valid motor FNOL on claimant.my's active policy.</summary>
    public static Dictionary<string, object?> MotorFnol(decimal estimate = 1500m, string description = "Smoke test") => new()
    {
        ["policyNumber"] = "POL-MY-M-0001",
        ["type"] = "Motor",
        ["incidentDate"] = Yesterday,
        ["incidentLocation"] = "Jalan Ampang, KL",
        ["description"] = description,
        ["estimatedLossAmount"] = estimate,
        ["motor"] = new { vehicleRegistration = "WXY 1234", thirdPartyInvolved = false, policeReportNumber = (string?)null },
    };

    public static async Task<NewClaim> Submit(decimal estimate = 1500m, string description = "Smoke test")
    {
        var created = await Api.Post("/api/claims", Users.ClaimantMy, MotorFnol(estimate, description));
        created.Status.ShouldBe(HttpStatusCode.Created, $"FNOL: {created}");
        return new NewClaim(Guid.Parse(created.Json["id"]!.GetValue<string>()), created.Json["claimNumber"]!.GetValue<string>());
    }

    /// <summary>Submitted and picked up by <paramref name="officer"/>: under review.</summary>
    public static async Task<NewClaim> UnderReview(string officer = Users.OfficerMy, decimal estimate = 1500m)
    {
        var claim = await Submit(estimate);
        (await Api.Post($"/api/claims/{claim.Id}/pick-up", officer)).Status.ShouldBe(HttpStatusCode.NoContent, "pick-up");
        return claim;
    }

    /// <summary>Under review, then the officer asks the claimant a question.</summary>
    public static async Task<NewClaim> AwaitingClaimant(string question = "Photos of the damage, please.")
    {
        var claim = await UnderReview();
        (await Api.Post($"/api/claims/{claim.Id}/information-requests", Users.OfficerMy, new { question }))
            .Status.ShouldBe(HttpStatusCode.NoContent, "request information");
        return claim;
    }

    public static async Task<JsonNode> Detail(Guid id, string asUser)
    {
        var detail = await Api.Get($"/api/claims/{id}", asUser);
        detail.Status.ShouldBe(HttpStatusCode.OK, $"claim detail as {asUser}: {detail}");
        return detail.Json;
    }

    /// <summary>
    /// Rejects a claim a test opened, so repeated runs don't pile open claims into the queues. Picks it
    /// up first if nobody has; <paramref name="assignee"/> is whoever has it.
    /// </summary>
    public static async Task Close(Guid id, string assignee = Users.OfficerMy)
    {
        var status = (await Detail(id, assignee))["status"]!.GetValue<string>();
        if (status == "Submitted") await Api.Post($"/api/claims/{id}/pick-up", assignee);
        if (status == "AwaitingInformation") await AnswerOpenRequest(id, "Closing the smoke test claim.");
        if (status is "Submitted" or "UnderReview" or "AwaitingInformation")
            (await Api.Post($"/api/claims/{id}/reject", assignee, new { reason = "Smoke test clean-up" }))
                .Status.ShouldBe(HttpStatusCode.NoContent, "clean-up reject");
    }

    public static async Task<ApiResponse> AnswerOpenRequest(Guid id, string response, string asUser = Users.ClaimantMy)
    {
        var request = (await Detail(id, Users.ClaimantMy))["openInformationRequest"]!["id"]!.GetValue<string>();
        return await Api.Post($"/api/claims/{id}/information-requests/{request}/response", asUser, new { response });
    }
}

/// <summary>Real file bytes, so a browser could show them too.</summary>
public static class Files
{
    /// <summary>A tiny orange PNG.</summary>
    public static byte[] Png(int width = 4, int height = 3)
    {
        var rows = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            rows.WriteByte(0); // no filter
            for (var x = 0; x < width; x++) rows.Write([0xF9, 0x73, 0x16]);
        }
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB

        var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", Deflate(rows.ToArray()));
        Chunk(png, "IEND", []);
        return png.ToArray();

        static void Chunk(Stream png, string type, byte[] data)
        {
            var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            var length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            var crc = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
            png.Write(length);
            png.Write(typed);
            png.Write(crc);
        }

        static uint Crc32(byte[] data) // PNG's CRC-32 (ISO 3309)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            return ~crc;
        }

        static byte[] Deflate(byte[] data)
        {
            var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal)) zlib.Write(data);
            return output.ToArray();
        }
    }

    public static readonly byte[] Pdf =
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");
}
