namespace ChubbClaims.Messaging.Settings;

/// <summary>Bound from "Kafka". Override BootstrapServers with Kafka__BootstrapServers in containers.</summary>
public sealed class KafkaSettings
{
    public const string SectionName = "Kafka";

    /// <summary>Off = no publisher/consumers (claims still work; events wait in the outbox).</summary>
    public bool Enabled { get; set; } = true;

    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "claims.events.v1";

    /// <summary>Partitions bound consumer parallelism; key = claimId keeps one claim's events in order.</summary>
    public int Partitions { get; set; } = 3;

    public int PublishBatchSize { get; set; } = 100;
    public int PublishIntervalMs { get; set; } = 1000;
}
