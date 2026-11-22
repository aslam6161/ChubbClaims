using ChubbClaims.Messaging;
using ChubbClaims.Messaging.Settings;
using ChubbClaims.Presentation.Extensions;
using ChubbClaims.Presentation.Hubs;
using ChubbClaims.Presentation.Middleware;
using ChubbClaims.Repository;
using ChubbClaims.Service;
using ChubbClaims.Service.Interfaces;
using ChubbClaims.Service.Storage;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

// Bootstrap logger so startup failures are logged before configuration is loaded.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, logger) =>
    {
        logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();

        // Readable text locally; one JSON object per line in containers (set LogFormat=Json).
        if (string.Equals(builder.Configuration["LogFormat"], "Json", StringComparison.OrdinalIgnoreCase))
            logger.WriteTo.Console(new RenderedCompactJsonFormatter());
        else
            logger.WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}");
    });

    builder.Services.AddPresentation(builder.Configuration);
    builder.Services.AddIdentityServices(builder.Configuration);
    builder.Services.AddRealtime();
    builder.Services.AddClaimsHealthChecks();
    builder.Services.AddServices();
    builder.Services.AddDocumentStorage(
        builder.Configuration.GetSection(DocumentStorageSettings.SectionName).Get<DocumentStorageSettings>() ?? new DocumentStorageSettings(),
        builder.Configuration.GetSection(CloudinarySettings.SectionName).Get<CloudinarySettings>() ?? new CloudinarySettings());
    builder.Services.AddRepository(builder.Configuration.GetConnectionString("ClaimsDb")
        ?? throw new InvalidOperationException("Connection string 'ClaimsDb' is not configured."));
    builder.Services.AddMessaging(builder.Configuration.GetSection(KafkaSettings.SectionName).Get<KafkaSettings>() ?? new KafkaSettings());

    var app = builder.Build();
    app.Logger.LogInformation("Claim documents are stored in {DocumentStorage}", app.Services.GetRequiredService<IDocumentStorage>().Name);

    if (app.Environment.IsDevelopment())
        await app.Services.InitialiseDatabaseAsync(seed: true); // migrate + seed demo data

    app.UseMiddleware<CorrelationIdMiddleware>(); // first, so every later log line has the id
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages(); // empty 4xx responses (e.g. 401/404 routing) also become ProblemDetails

    app.UseCors(PresentationServiceExtensions.FrontendCorsPolicy);
    app.UseAuthentication();
    app.UseAuthorization();

    if (app.Environment.IsDevelopment())
    {
        // AllowAnonymous: the fallback policy requires auth everywhere else.
        app.MapOpenApi().AllowAnonymous(); // /openapi/v1.json (the contract the frontend generates types from)
        app.MapScalarApiReference(o => o.WithTitle("Claims Platform API")).AllowAnonymous(); // /scalar
    }

    app.MapHealthChecks(HealthCheckExtensions.Path).AllowAnonymous(); // for Docker / load balancers
    app.MapControllers();
    app.MapHub<ClaimsHub>(ClaimsHub.Path); // [Authorize] on the hub; JWT via access_token for WebSockets

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException) // HostAbortedException is thrown by EF design-time tooling
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
