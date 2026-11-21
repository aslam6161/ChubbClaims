using ChubbClaims.Messaging.Settings;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace ChubbClaims.Messaging.Topics;

/// <summary>
/// Creates claims.events.v1 with the configured partitions if it does not exist. Called by the
/// publisher and consumers before they start; it never blocks API startup, so claims are accepted
/// even while Kafka is down.
/// </summary>
public sealed class KafkaTopicInitializer(KafkaSettings settings, ILogger<KafkaTopicInitializer> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _ready;

    /// <summary>Retries until the topic exists or <paramref name="ct"/> is cancelled.</summary>
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        if (_ready) return;
        await _lock.WaitAsync(ct);
        try
        {
            for (var attempt = 1; !_ready; attempt++)
            {
                try
                {
                    using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = settings.BootstrapServers }).Build();
                    await admin.CreateTopicsAsync([new TopicSpecification { Name = settings.Topic, NumPartitions = settings.Partitions, ReplicationFactor = 1 }]);
                    logger.LogInformation("Created Kafka topic {Topic} with {Partitions} partitions", settings.Topic, settings.Partitions);
                    _ready = true;
                }
                catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
                {
                    _ready = true;
                }
                catch (KafkaException ex)
                {
                    logger.LogWarning("Kafka not reachable at {Servers} (attempt {Attempt}): {Reason}", settings.BootstrapServers, attempt, ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * attempt)), ct);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
