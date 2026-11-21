using System.Text;
using ChubbClaims.Messaging.Settings;
using ChubbClaims.Repository.Outbox;
using Confluent.Kafka;

namespace ChubbClaims.Messaging.Producers;

public interface IEventProducer
{
    /// <summary>Publishes and waits for the broker acknowledgement; throws if it is not acknowledged.</summary>
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

public static class KafkaHeaders
{
    public const string EventType = "event-type";
    public const string EventId = "event-id";
    public const string CorrelationId = "correlation-id";
}

/// <summary>
/// One long-lived producer (thread-safe, owns the connection and batching).
/// Acks=All + idempotence: a broker retry cannot duplicate or reorder messages within a partition.
/// </summary>
public sealed class KafkaEventProducer : IEventProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    public KafkaEventProducer(KafkaSettings settings)
    {
        _topic = settings.Topic;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            ClientId = "chubbclaims-outbox-publisher",
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000, // fail fast when Kafka is down; the outbox keeps the event
        }).Build();
    }

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        var headers = new Headers
        {
            { KafkaHeaders.EventType, Encoding.UTF8.GetBytes(message.EventType) },
            { KafkaHeaders.EventId, Encoding.UTF8.GetBytes(message.Id.ToString()) },
        };
        if (message.CorrelationId is not null)
            headers.Add(KafkaHeaders.CorrelationId, Encoding.UTF8.GetBytes(message.CorrelationId));

        await _producer.ProduceAsync(_topic, new Message<string, string>
        {
            Key = message.ClaimId.ToString(), // all events of a claim -> same partition -> in order
            Value = message.Payload,
            Headers = headers,
        }, ct);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
