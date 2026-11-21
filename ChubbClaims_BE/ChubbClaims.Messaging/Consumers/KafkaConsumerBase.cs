using System.Text;
using System.Text.Json;
using ChubbClaims.Messaging.Contracts;
using ChubbClaims.Messaging.Producers;
using ChubbClaims.Messaging.Settings;
using ChubbClaims.Messaging.Topics;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChubbClaims.Messaging.Consumers;

/// <summary>
/// Consume loop shared by all consumers:
/// - its own consumer group, so each consumer sees every event;
/// - auto-commit off: the offset is committed only after the handler succeeded (at-least-once);
/// - handlers must be idempotent (record the event id with the side effect);
/// - a message that is not a valid envelope is logged and skipped (it can never succeed);
///   a handler failure (e.g. DB down) is retried with backoff and blocks the partition, so no
///   event is lost or applied out of order. A dead-letter topic is a "more time" item.
/// </summary>
public abstract class KafkaConsumerBase(
    IServiceScopeFactory scopes, KafkaTopicInitializer topics, KafkaSettings settings, ILogger logger) : BackgroundService
{
    protected KafkaSettings Settings { get; } = settings;

    protected abstract string ConsumerGroup { get; }

    protected abstract Task HandleAsync(EventEnvelope envelope, IServiceProvider services, CancellationToken ct);

    // Consume() blocks, so run the loop on its own thread instead of the host's startup path.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken ct)
    {
        await topics.EnsureCreatedAsync(ct);

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = Settings.BootstrapServers,
            GroupId = ConsumerGroup,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest, // a new group starts from the beginning of the topic
        }).Build();
        consumer.Subscribe(Settings.Topic);
        logger.LogInformation("Consumer group {ConsumerGroup} subscribed to {Topic}", ConsumerGroup, Settings.Topic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(ct);
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning("Consume error in {ConsumerGroup}: {Reason}", ConsumerGroup, ex.Error.Reason);
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    continue;
                }

                await ProcessAsync(result, ct);
                consumer.Commit(result); // only after the side effect is committed
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        finally
        {
            consumer.Close(); // leave the group cleanly so partitions rebalance immediately
        }
    }

    private async Task ProcessAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        using var _ = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = Header(result, KafkaHeaders.CorrelationId),
            ["ConsumerGroup"] = ConsumerGroup,
            ["KafkaOffset"] = result.TopicPartitionOffset.ToString(),
        });

        EventEnvelope envelope;
        try
        {
            envelope = EventEnvelope.Parse(result.Message.Value);
        }
        catch (JsonException ex)
        {
            logger.LogError("Skipping message at {Offset}: not a valid event envelope ({Reason})", result.TopicPartitionOffset, ex.Message);
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await HandleAsync(envelope, scope.ServiceProvider, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                logger.LogWarning(ex, "{EventType} {EventId} failed in {ConsumerGroup} (attempt {Attempt}); retrying in {Delay}",
                    envelope.EventType, envelope.EventId, ConsumerGroup, attempt, delay);
                await Task.Delay(delay, ct);
            }
        }
    }

    private static string? Header(ConsumeResult<string, string> result, string name) =>
        result.Message.Headers?.TryGetLastBytes(name, out var bytes) == true ? Encoding.UTF8.GetString(bytes) : null;
}
