using ChubbClaims.SmokeTests.Infrastructure;
using static ChubbClaims.SmokeTests.Infrastructure.Users;

namespace ChubbClaims.SmokeTests;

/// <summary>Sign-in, first notice of loss, what a claimant may see, and answering a question.</summary>
public class ClaimantFlowTests
{
    [SmokeFact]
    public async Task No_token_gets_401_problem_details()
    {
        var response = await Api.Get("/api/claims/mine");
        response.Status.ShouldBe(HttpStatusCode.Unauthorized);
        response.Json.Int("status").ShouldBe(401);
    }

    [SmokeFact]
    public async Task Dev_login_lists_the_nine_seeded_users_and_refuses_an_unknown_email()
    {
        var users = await Api.Get("/api/auth/dev-users");
        users.Json.AsArray().Count.ShouldBe(9);
        (await Api.Post("/api/auth/dev-login", null, new { email = "nobody@demo.local" })).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [SmokeFact]
    public async Task Me_and_my_policies()
    {
        var me = (await Api.Get("/api/me", ClaimantMy)).Json;
        me.Str("role").ShouldBe("Claimant");
        me.IsNull("market").ShouldBeTrue("a claimant has no market");

        var policies = (await Api.Get("/api/policies/mine", ClaimantMy)).Json.AsArray();
        policies.Count.ShouldBe(3, "motor, property and an expired one");
    }

    [SmokeFact]
    public async Task FNOL_creates_a_claim_the_claimant_sees_without_reserve_and_staff_see_with_it()
    {
        var created = await Api.Post("/api/claims", ClaimantMy, Claims.MotorFnol(7300.50m));
        created.Status.ShouldBe(HttpStatusCode.Created, created.ToString());
        created.Json.Str("claimNumber").ShouldStartWith("CLM-MY-");
        created.Response.Headers.Location.ShouldNotBeNull();
        var id = created.Json.Id();

        var mine = await Api.Get("/api/claims/mine", ClaimantMy);
        mine.Json.List().ShouldContain(c => c.Id("id") == id, "my claims include the new one");

        var claimantView = await Claims.Detail(id, ClaimantMy);
        claimantView.IsNull("reserveAmount").ShouldBeTrue("the claimant doesn't see the reserve");
        claimantView.IsNull("notes").ShouldBeTrue("nor internal notes");

        (await Claims.Detail(id, OfficerMy)).Num("reserveAmount").ShouldBe(7300.50m, "the MY officer sees the reserve");

        await Claims.Close(id);
    }

    public static TheoryData<string, string, string, int> RefusedFnols => new()
    {
        { "an expired policy", "policyNumber", "\"POL-MY-M-0000\"", 422 },
        { "someone else's policy", "policyNumber", "\"POL-SG-M-0001\"", 404 },
        { "an incident in the future", "incidentDate", $"\"{DateTime.Today.AddDays(2):yyyy-MM-dd}\"", 422 },
        { "a motor claim without vehicle details", "motor", "null", 422 },
    };

    [SmokeTheory]
    [MemberData(nameof(RefusedFnols))]
    public async Task FNOL_is_refused_for(string why, string field, string json, int expected)
    {
        var body = Claims.MotorFnol();
        body[field] = JsonNode.Parse(json);
        var response = await Api.Post("/api/claims", ClaimantMy, body);
        ((int)response.Status).ShouldBe(expected, $"{why}: {response}");
    }

    [SmokeFact]
    public async Task FNOL_with_missing_fields_gets_400_with_field_errors()
    {
        var response = await Api.Post("/api/claims", ClaimantMy, new { policyNumber = "POL-MY-M-0001" });
        response.Status.ShouldBe(HttpStatusCode.BadRequest);
        response.Json["errors"].ShouldNotBeNull();
    }

    [SmokeFact]
    public async Task Banner_counts_every_claim_waiting_on_the_claimant_and_links_the_oldest()
    {
        var claim = await Claims.AwaitingClaimant();

        var page = (await Api.Get("/api/claims/mine?pageSize=100", ClaimantMy)).Json;
        var all = page.List().ToList();
        for (var p = 2; all.Count < page.Int("totalCount"); p++)
            all.AddRange((await Api.Get($"/api/claims/mine?pageSize=100&page={p}", ClaimantMy)).Json.List());
        var awaiting = all.Where(c => c.Str("status") == "AwaitingInformation").ToList();

        awaiting.ShouldContain(c => c.Id("id") == claim.Id);
        page.Int("awaitingYouCount").ShouldBe(awaiting.Count);
        page.Id("oldestAwaitingYouClaimId").ShouldBe(awaiting[^1].Id("id"), "the list is newest first, so the oldest is last");

        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task Search_matches_the_description_ignoring_case_and_never_reaches_another_claimant()
    {
        var word = $"Kitchen{Guid.NewGuid():N}"[..14];
        var claim = await Claims.Submit(description: $"Water leak under the {word} sink");

        var found = (await Api.Get($"/api/claims/mine?search={word.ToLowerInvariant()}", ClaimantMy)).Json;
        found.List().Select(c => c.Str("claimNumber")).ShouldBe(new[] { claim.Number });

        var theirs = (await Api.Get("/api/claims/mine?search=CLM-MY", ClaimantSg)).Json;
        theirs.Int("totalCount").ShouldBe(0, "claimant.sg has no MY claims and must not see claimant.my's");

        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task Someone_else_reading_the_claim_gets_404_not_403()
    {
        var claim = await Claims.Submit();
        (await Api.Get($"/api/claims/{claim.Id}", ClaimantSg)).Status.ShouldBe(HttpStatusCode.NotFound, "another claimant");
        (await Api.Get($"/api/claims/{claim.Id}", OfficerSg)).Status.ShouldBe(HttpStatusCode.NotFound, "an officer in another market");
        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task Claimant_answers_the_officers_question_once()
    {
        var claim = await Claims.AwaitingClaimant();

        (await Claims.AnswerOpenRequest(claim.Id, "x", asUser: ClaimantSg)).Status
            .ShouldBe(HttpStatusCode.NotFound, "another claimant answering");

        var requestId = (await Claims.Detail(claim.Id, ClaimantMy))["openInformationRequest"]!.Id();
        var answer = $"/api/claims/{claim.Id}/information-requests/{requestId}/response";
        (await Api.Post(answer, ClaimantMy, new { response = "Report and photos uploaded." })).Status.ShouldBe(HttpStatusCode.NoContent);

        var after = await Claims.Detail(claim.Id, ClaimantMy);
        after.Str("status").ShouldBe("UnderReview");
        after.IsNull("openInformationRequest").ShouldBeTrue();

        var again = await Api.Post(answer, ClaimantMy, new { response = "again" });
        again.Status.ShouldBe(HttpStatusCode.Conflict, "answering twice");

        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task Staff_cannot_use_the_claimant_endpoints()
    {
        (await Api.Post("/api/claims", OfficerMy, Claims.MotorFnol())).Status.ShouldBe(HttpStatusCode.Forbidden, "officer submitting an FNOL");
        (await Api.Get("/api/policies/mine", OfficerMy)).Status.ShouldBe(HttpStatusCode.Forbidden, "officer listing claimant policies");
    }
}
