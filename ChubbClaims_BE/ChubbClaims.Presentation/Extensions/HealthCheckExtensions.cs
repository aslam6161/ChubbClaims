using ChubbClaims.Repository;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ChubbClaims.Presentation.Extensions;

public static class HealthCheckExtensions
{
    public const string Path = "/health";

    /// <summary>
    /// Readiness: the database must be reachable. Kafka is deliberately not checked: the API keeps
    /// accepting claims while Kafka is down (events wait in the outbox), so Kafka being down must
    /// not take the API out of a load balancer.
    /// </summary>
    public static IServiceCollection AddClaimsHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        return services;
    }
}

internal sealed class DatabaseHealthCheck(DataContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to SQL Server.");
}
