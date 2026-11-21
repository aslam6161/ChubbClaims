using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Domain.Entity;

// Child entities of the Claim aggregate. They are created and changed only through Claim methods
// (internal constructors), so the aggregate's rules cannot be bypassed.

/// <summary>A question from the officer to the claimant. At most one is open per claim.</summary>
public sealed class InformationRequest : BaseEntity
{
    private InformationRequest() { } // EF Core

    internal InformationRequest(Guid claimId, Guid requestedById, string question, DateTimeOffset requestedAt)
    {
        ClaimId = claimId;
        RequestedById = requestedById;
        Question = question;
        RequestedAt = requestedAt;
    }

    public Guid ClaimId { get; private set; }
    public Guid RequestedById { get; private set; }
    public string Question { get; private set; } = null!;
    public DateTimeOffset RequestedAt { get; private set; }
    public string? Response { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }
    public bool IsOpen => RespondedAt is null;

    internal void Answer(string response, DateTimeOffset at)
    {
        Response = response;
        RespondedAt = at;
    }
}

/// <summary>Append-only audit row for every status change, including submission (From = null).</summary>
public sealed class ClaimStatusChange : BaseEntity
{
    private ClaimStatusChange() { } // EF Core

    internal ClaimStatusChange(
        Guid claimId, ClaimStatus? from, ClaimStatus to, Guid actorId, UserRole actorRole, DateTimeOffset at, string? reason)
    {
        ClaimId = claimId;
        From = from;
        To = to;
        ActorId = actorId;
        ActorRole = actorRole;
        At = at;
        Reason = reason;
    }

    public Guid ClaimId { get; private set; }
    public ClaimStatus? From { get; private set; }
    public ClaimStatus To { get; private set; }
    public Guid ActorId { get; private set; }
    public UserRole ActorRole { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string? Reason { get; private set; }
}

/// <summary>Append-only audit row for every reserve change, including the initial reserve.</summary>
public sealed class ReserveChange : BaseEntity
{
    private ReserveChange() { } // EF Core

    internal ReserveChange(Guid claimId, decimal oldAmount, decimal newAmount, Guid actorId, DateTimeOffset at, string reason)
    {
        ClaimId = claimId;
        OldAmount = oldAmount;
        NewAmount = newAmount;
        ActorId = actorId;
        At = at;
        Reason = reason;
    }

    public Guid ClaimId { get; private set; }
    public decimal OldAmount { get; private set; }
    public decimal NewAmount { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string Reason { get; private set; } = null!;
}

/// <summary>Internal staff note on a claim (not shown to the claimant).</summary>
public sealed class ClaimNote : BaseEntity
{
    private ClaimNote() { } // EF Core

    internal ClaimNote(Guid claimId, Guid authorId, string text, DateTimeOffset createdAt)
    {
        ClaimId = claimId;
        AuthorId = authorId;
        Text = text;
        CreatedAt = createdAt;
    }

    public Guid ClaimId { get; private set; }
    public Guid AuthorId { get; private set; }
    public string Text { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}
