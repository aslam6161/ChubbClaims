using ChubbClaims.Messaging.Contracts;
using ChubbClaims.Messaging.Projections;
using ChubbClaims.Messaging.Realtime;
using ChubbClaims.Messaging.Settings;
using ChubbClaims.Messaging.Topics;
using ChubbClaims.Repository.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChubbClaims.Messaging.Consumers;

/// <summary>Maintains the ExposureByMarket read model from claim events, applying each event exactly once.</summary>
public sealed class ExposureProjectorConsumer(
    IServiceScopeFactory scopes, KafkaTopicInitializer topics, KafkaSettings settings, IRealtimePush push,
    ILogger<ExposureProjectorConsumer> logger)
    : KafkaConsumerBase(scopes, topics, settings, logger)
{
    public const string GroupId = "exposure-projector";

    protected override string ConsumerGroup => GroupId;

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider services, CancellationToken ct)
    {
        var delta = ExposureProjection.DeltaFor(envelope);
        if (delta is null) return; // event does not change exposure

        var applied = await services.GetRequiredService<IExposureRepository>().ApplyOnceAsync(
            GroupId, envelope.EventId, new ExposureKey(envelope.Market, envelope.Currency, envelope.ClaimType), delta, envelope.OccurredAt, ct);

        if (applied)
        {
            await PushAsync(envelope, ct);
            logger.LogDebug("Exposure {Market}/{Currency}/{ClaimType}: {OpenDelta:+0;-0;0} open, {Delta:+0.00;-0.00;0} outstanding ({EventType} {ClaimNumber})",
                envelope.Market, envelope.Currency, envelope.ClaimType, delta.OpenClaims, delta.OutstandingReserve, envelope.EventType, envelope.ClaimNumber);
        }
        else
            logger.LogDebug("Duplicate {EventType} {EventId} skipped by {ConsumerGroup}", envelope.EventType, envelope.EventId, GroupId);
    }

    private async Task PushAsync(EventEnvelope envelope, CancellationToken ct)
    {
        try
        {
            await push.ExposureUpdatedAsync(new ExposureUpdatedMessage(envelope.Market, envelope.Currency, envelope.ClaimType, envelope.OccurredAt), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ExposureUpdated push failed"); // a hint only; the read model is already updated
        }
    }
}
