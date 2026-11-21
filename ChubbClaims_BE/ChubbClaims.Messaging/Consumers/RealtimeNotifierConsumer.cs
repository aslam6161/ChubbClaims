using ChubbClaims.Messaging.Contracts;
using ChubbClaims.Messaging.Projections;
using ChubbClaims.Messaging.Realtime;
using ChubbClaims.Messaging.Settings;
using ChubbClaims.Messaging.Topics;
using ChubbClaims.Repository.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChubbClaims.Messaging.Consumers;

/// <summary>
/// Stores in-app notifications for each claim event (exactly once) and then pushes a
/// ClaimUpdated hint over SignalR. Its own consumer group, so it is independent of the projector:
/// a slow or failing notifier never delays exposure, and vice versa.
/// </summary>
public sealed class RealtimeNotifierConsumer(
    IServiceScopeFactory scopes, KafkaTopicInitializer topics, KafkaSettings settings, IRealtimePush push,
    TimeProvider clock, ILogger<RealtimeNotifierConsumer> logger)
    : KafkaConsumerBase(scopes, topics, settings, logger)
{
    public const string GroupId = "realtime-notifier";

    protected override string ConsumerGroup => GroupId;

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider services, CancellationToken ct)
    {
        if (clock.GetUtcNow() - envelope.OccurredAt > TimeSpan.FromMinutes(Settings.NotificationMaxAgeMinutes))
        {
            logger.LogDebug("Skipping stale {EventType} {EventId} from {OccurredAt}", envelope.EventType, envelope.EventId, envelope.OccurredAt);
            return;
        }

        var notifications = NotificationProjection.For(envelope);
        var isNew = await services.GetRequiredService<INotificationRepository>().AddOnceAsync(GroupId, envelope.EventId, notifications, ct);
        if (!isNew)
        {
            logger.LogDebug("Duplicate {EventType} {EventId} skipped by {ConsumerGroup}", envelope.EventType, envelope.EventId, GroupId);
            return; // already notified and pushed
        }

        try
        {
            await push.ClaimUpdatedAsync(new ClaimUpdatedMessage(
                envelope.ClaimId, envelope.ClaimNumber, envelope.Market, envelope.EventType,
                ClaimEventTypes.StatusAfter(envelope.EventType), envelope.OccurredAt), envelope.ClaimantId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A push is a hint; the notification is stored and REST is the source of truth.
            logger.LogWarning(ex, "Push for {EventType} {ClaimNumber} failed", envelope.EventType, envelope.ClaimNumber);
        }

        logger.LogInformation("{EventType} {ClaimNumber}: {Count} notification(s) stored, update pushed",
            envelope.EventType, envelope.ClaimNumber, notifications.Count);
    }
}
