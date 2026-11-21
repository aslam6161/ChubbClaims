namespace ChubbClaims.Service.DTOs.Notifications;

public sealed record NotificationListResponse(int UnreadCount, IReadOnlyList<NotificationResponse> Items);

public sealed record NotificationResponse(
    Guid Id, Guid ClaimId, string ClaimNumber, string Message, DateTimeOffset CreatedAt, bool IsRead);
