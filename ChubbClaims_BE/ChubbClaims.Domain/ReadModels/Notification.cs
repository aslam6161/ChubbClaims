namespace ChubbClaims.Domain.ReadModels;

/// <summary>
/// An in-app notification ("your claim was approved"). Written only by the realtime notifier
/// from Kafka events; users can only mark it read.
/// </summary>
public sealed class Notification
{
    public const int MaxMessageLength = 500;

    private Notification() { } // EF Core

    public Notification(Guid userId, Guid claimId, string claimNumber, string message, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        ClaimId = claimId;
        ClaimNumber = claimNumber;
        Message = message.Length > MaxMessageLength ? message[..(MaxMessageLength - 1)] + "…" : message;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ClaimId { get; private set; }
    public string ClaimNumber { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    public void MarkRead(DateTimeOffset at) => ReadAt ??= at;
}
