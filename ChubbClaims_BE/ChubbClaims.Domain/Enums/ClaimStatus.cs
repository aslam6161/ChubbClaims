namespace ChubbClaims.Domain.Enums;

public enum ClaimStatus
{
    Submitted = 1,
    UnderReview = 2,
    AwaitingInformation = 3,
    Approved = 4,
    Settled = 5,  // closed
    Rejected = 6, // closed
}
