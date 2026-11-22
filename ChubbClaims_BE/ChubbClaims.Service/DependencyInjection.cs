using ChubbClaims.Service.Interfaces;
using ChubbClaims.Service.Services;
using ChubbClaims.Service.Storage;
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
        services.AddScoped<IClaimDocumentService, ClaimDocumentService>();
        services.AddScoped<IWorkQueueService, WorkQueueService>();
        services.AddScoped<IExposureService, ExposureService>();
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<INotificationService, NotificationService>();
        return services;
    }

    /// <summary>
    /// Cloudinary when its credentials are configured, local disk otherwise. A missing credential
    /// never stops the API: a fresh clone and CI run without one, and attaching still works.
    /// </summary>
    public static IServiceCollection AddDocumentStorage(
        this IServiceCollection services, DocumentStorageSettings local, CloudinarySettings cloudinary)
    {
        services.AddSingleton(local);
        services.AddSingleton(cloudinary);
        // Singletons: the Cloudinary client and its HttpClient are thread-safe and meant to be reused.
        if (cloudinary.IsConfigured)
            services.AddSingleton<IDocumentStorage, CloudinaryDocumentStorage>();
        else
            services.AddSingleton<IDocumentStorage, LocalDiskDocumentStorage>();
        return services;
    }
}
