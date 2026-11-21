using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Rules;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Domain;

public class ClaimProgressTests
{
    [Theory]
    [InlineData(ClaimStatus.Submitted, WaitingOn.Officer)]
    [InlineData(ClaimStatus.UnderReview, WaitingOn.Officer)]
    [InlineData(ClaimStatus.AwaitingInformation, WaitingOn.Claimant)]
    [InlineData(ClaimStatus.Approved, WaitingOn.Officer)] // payment still to be made
    [InlineData(ClaimStatus.Settled, WaitingOn.Nobody)]
    [InlineData(ClaimStatus.Rejected, WaitingOn.Nobody)]
    public void Each_status_waits_on_the_right_party(ClaimStatus status, WaitingOn expected)
    {
        ClaimProgress.WaitingOnFor(status).ShouldBe(expected);
    }

    [Fact]
    public void Overdue_means_sla_passed_without_a_decision()
    {
        ClaimProgress.IsOverdue(slaDueAt: Now.AddMinutes(-1), decisionAt: null, Now).ShouldBeTrue();
        ClaimProgress.IsOverdue(slaDueAt: Now.AddMinutes(1), decisionAt: null, Now).ShouldBeFalse();
        ClaimProgress.IsOverdue(slaDueAt: Now.AddDays(-1), decisionAt: Now.AddDays(-2), Now).ShouldBeFalse();
    }
}
