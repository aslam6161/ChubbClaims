using ChubbClaims.Messaging.Producers;
using ChubbClaims.Messaging.Settings;
using ChubbClaims.Messaging.Topics;
using ChubbClaims.Repository.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChubbClaims.Messaging.Publishers;

/// <summary>
/// One publishing pass: send unpublished outbox messages to Kafka in insertion order and mark them.
/// Delivery is at-least-once: if marking fails after a successful send, the message is sent again
/// next pass and consumers drop the duplicate by event id.
/// </summary>
public sealed class OutboxPublisher(
    IOutboxRepository outbox, IUnitOfWork unitOfWork, IEventProducer producer, KafkaSettings settings,
    TimeProvider clock, ILogger<OutboxPublisher> logger)
{
    /// <returns>How many messages were published.</returns>
    public async Task<int> PublishPendingAsync(CancellationToken ct)
    {
        var batch = await outbox.GetUnpublishedAsync(settings.PublishBatchSize, ct);
        if (batch.Count == 0) return 0;

        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                await producer.PublishAsync(message, ct);
                message.MarkPublished(clock.GetUtcNow());
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Stop here: publishing later events first would break per-claim ordering.
                message.MarkFailed(ex.Message);
                logger.LogWarning("Publishing {EventType} {EventId} failed (attempt {Attempts}): {Reason}",
                    message.EventType, message.Id, message.Attempts, ex.Message);
                break;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        if (published > 0)
            logger.LogInformation("Published {Count} outbox event(s) to {Topic}", published, settings.Topic);
        return published;
    }
}

/// <summary>Runs <see cref="OutboxPublisher"/> in the background, polling while there is nothing to send.</summary>
public sealed class OutboxPublisherService(
    IServiceScopeFactory scopes, KafkaTopicInitializer topics, KafkaSettings settings, ILogger<OutboxPublisherService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // let the host finish starting
        await topics.EnsureCreatedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                published = await scope.ServiceProvider.GetRequiredService<OutboxPublisher>().PublishPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox publishing pass failed"); // e.g. database unavailable; try again next pass
            }

            // A full batch means more may be waiting: go again straight away.
            if (published < settings.PublishBatchSize)
                await Task.Delay(settings.PublishIntervalMs, stoppingToken);
        }
    }
}
