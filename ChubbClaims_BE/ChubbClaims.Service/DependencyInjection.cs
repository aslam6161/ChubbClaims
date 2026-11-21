using ChubbClaims.Service.Interfaces;
using ChubbClaims.Service.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ChubbClaims.Service;

public static class DependencyInjection
{
    /// <summary>Application services (use cases). The host must register <see cref="ICurrentUser"/>.</summary>
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IPolicyService, PolicyService>();
        services.AddScoped<IClaimService, ClaimService>();
        services.AddScoped<IWorkQueueService, WorkQueueService>();
        return services;
    }
}
