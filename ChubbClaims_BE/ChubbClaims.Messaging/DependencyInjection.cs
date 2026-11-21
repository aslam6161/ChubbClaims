using ChubbClaims.Messaging.Consumers;
using ChubbClaims.Messaging.Producers;
using ChubbClaims.Messaging.Publishers;
using ChubbClaims.Messaging.Settings;
using ChubbClaims.Messaging.Topics;
using Microsoft.Extensions.DependencyInjection;

namespace ChubbClaims.Messaging;

public static class DependencyInjection
{
    /// <summary>
    /// Kafka: outbox publisher and consumers as hosted services in this process. This project is the
    /// seam where the projector or a notifier could be lifted into its own deployable later.
    /// </summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, KafkaSettings settings)
    {
        services.AddSingleton(settings);
        if (!settings.Enabled)
            return services; // events stay in the outbox until Kafka is enabled

        services.AddSingleton<KafkaTopicInitializer>();
        services.AddSingleton<IEventProducer, KafkaEventProducer>();
        services.AddScoped<OutboxPublisher>();
        services.AddHostedService<OutboxPublisherService>();
        services.AddHostedService<ExposureProjectorConsumer>();
        return services;
    }
}
