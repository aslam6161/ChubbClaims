using System.Text.Json.Serialization;
using ChubbClaims.Messaging.Realtime;
using ChubbClaims.Presentation.Hubs;

namespace ChubbClaims.Presentation.Extensions;

public static class RealtimeServiceExtensions
{
    /// <summary>SignalR hub and the push port the Kafka consumers use.</summary>
    public static IServiceCollection AddRealtime(this IServiceCollection services)
    {
        services.AddSignalR()
            // Same enum format as the REST API ("Approved", not 4).
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddSingleton<IRealtimePush, SignalRRealtimePush>();
        return services;
    }
}
