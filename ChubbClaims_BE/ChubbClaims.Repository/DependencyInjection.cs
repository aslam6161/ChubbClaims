using ChubbClaims.Repository.Data;
using ChubbClaims.Repository.Interceptors;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Repository.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChubbClaims.Repository;

public static class DependencyInjection
{
    /// <summary>
    /// Persistence: DbContext (SQL Server) with the outbox interceptor, repositories, unit of work.
    /// The host must register an <see cref="ICorrelationContext"/>.
    /// </summary>
    public static IServiceCollection AddRepository(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<DomainEventsToOutboxInterceptor>();
        services.AddDbContext<DataContext>((sp, options) => options
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 5))
            .AddInterceptors(sp.GetRequiredService<DomainEventsToOutboxInterceptor>()));

        services.AddScoped<IClaimRepository, ClaimRepository>();
        services.AddScoped<IPolicyRepository, PolicyRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IExposureRepository, ExposureRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<DevDataSeeder>();
        return services;
    }

    /// <summary>
    /// Development startup: apply migrations (waiting for SQL Server to accept connections) and seed.
    /// Production would run migrations as a separate deployment step, not on app start.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services, bool seed, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DataContext>>();

        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                break;
            }
            catch (SqlException ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("SQL Server not ready (attempt {Attempt}/{Max}): {Message}", attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }

        if (seed)
            await scope.ServiceProvider.GetRequiredService<DevDataSeeder>().SeedAsync(ct);
    }
}
