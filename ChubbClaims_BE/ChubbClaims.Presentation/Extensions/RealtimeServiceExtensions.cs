using ChubbClaims.Messaging.Realtime;
using ChubbClaims.Presentation.Hubs;

namespace ChubbClaims.Presentation.Extensions;

public static class RealtimeServiceExtensions
{
    /// <summary>SignalR hub and the push port the Kafka consumers use.</summary>
    public static IServiceCollection AddRealtime(this IServiceCollection services)
    {
        services.AddSignalR()
            .AddJsonProtocol(o => ApiJson.Configure(o.PayloadSerializerOptions)); // same format as the REST API
        services.AddSingleton<IRealtimePush, SignalRRealtimePush>();
        return services;
    }
}
