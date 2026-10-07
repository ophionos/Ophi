using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.HealthChecks;
using Ophi.Api.Common.Middleware;
using Ophi.Api.Common.Startup;
using Ophi.Domain.Entities;
using Ophi.Infrastructure;
using Ophi.Infrastructure.Metrics;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Ophi.Worker.Configuration;
using Ophi.Worker.Services;
using Ophi.Worker.Settings;
using Prometheus;
using Scalar.AspNetCore;
using Wolverine;
using Wolverine.FluentValidation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

var enableWorker = builder.Configuration.GetValue<bool>("ENABLE_WORKER");
builder.Host.UseWolverine(opts =>
{
    // Shared Wolverine config (queues, retries, Postgres persistence/transport, routing) lives in
    // WolverineConfig so the API and Worker can't drift. Embedded = in-process worker (Pi); otherwise
    // the API only routes ScrapeProductUrlCommand out to the worker over the Postgres transport.
    var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? builder.Configuration["POSTGRES_CONNECTION"];
    WolverineConfig.Configure(
        opts,
        enableWorker ? WolverineMode.Embedded : WolverineMode.SplitApi,
        postgresConnectionString);

    opts.UseFluentValidation();
});

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// In-memory SSE connection registry — tracks the live EventSource streams this API instance hosts,
// fanned out to by PushLiveUpdateHandler when a LiveUpdate arrives (in-process or over the transport).
builder.Services.AddSingleton<Ophi.Api.Common.Events.ISseConnectionRegistry, Ophi.Api.Common.Events.SseConnectionRegistry>();

// TimeProvider — single abstraction for "what time is it" so tests can fake it.
// Production uses TimeProvider.System; tests substitute FakeTimeProvider in their
// WebApplicationFactory override.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Ophi API";
        document.Info.Version = "v1";
        document.Info.Description = "Price tracking and comparison API. Cookie-based authentication required for all endpoints except /auth/*.";
        return Task.CompletedTask;
    });
});

builder.Services.Configure<AlertSettings>(
    builder.Configuration.GetSection(AlertSettings.SectionName));
builder.Services.Configure<RegistrationSettings>(
    builder.Configuration.GetSection(RegistrationSettings.SectionName));

// Conditionally enable embedded Worker (for Pi / single-container deployments)
if (enableWorker)
{
    builder.Services.Configure<WorkerSettings>(
        builder.Configuration.GetSection(WorkerSettings.SectionName));
    builder.Services.AddSingleton<DataRetentionService>();
    builder.Services.AddSingleton<ExchangeRateRefresher>();
    builder.Services.AddHostedService<PriceCheckDispatcher>();
    builder.Services.AddHostedService<PriceMetricsCollector>();
}

builder.Services.AddOphiAuth(builder.Environment, builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var appUrl = builder.Configuration["APP_URL"];
        var origins = !string.IsNullOrEmpty(appUrl)
            ? appUrl.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ["http://localhost:5173", "http://localhost:3000"];

        policy.WithOrigins(origins)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithHeaders("Content-Type", "Accept", "X-Requested-With")
            .AllowCredentials();
    });
});

// EnableForHttps left as default false to avoid BREACH-type attacks
builder.Services.AddResponseCompression();

builder.Services
    .AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<WolverineHealthCheck>("wolverine", tags: ["ready"])
    .AddCheck<ScrapeHealthCheck>("scraping", HealthStatus.Degraded, ["ready"]);

builder.Services.AddOphiForwardedHeaders(builder.Configuration);
builder.Services.AddOphiRateLimiting(builder.Environment);

var app = builder.Build();

// Ensure database is created and migrated (skip in testing environment)
if (!app.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
    dbContext.Database.Migrate();
}

// OpenAPI spec at /openapi/v1.json, Scalar UI at /scalar/v1
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// First, so everything downstream — rate-limit partitions, request logging — sees the real client
// IP rather than the proxy (SvelteKit hook on compose, Caddy on the Pi). Trust model: ForwardedHeadersSetup.
app.UseForwardedHeaders();

// Configure middleware (exception handling before compression so error responses are not partially compressed)
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseResponseCompression();

// Skip CSRF and rate limiting in integration test environment
if (!app.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
{
    app.UseMiddleware<CsrfMiddleware>();
    app.UseRateLimiter();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.UseHttpMetrics(); // Prometheus HTTP request metrics
app.UseMiddleware<RequestLoggingMiddleware>();

// After UseAuthentication so the principal exists, and after metrics/logging so the 403s it
// short-circuits are still counted and logged — a silently-rejected key is the hardest kind to
// debug. NOT skipped in Testing (unlike CSRF/rate limiting): it is a real authorization control
// and the integration tests assert it.
app.UseMiddleware<ApiKeyScopeMiddleware>();

app.MapOphiEndpoints();
app.MapHealthAndMetrics();

app.Run();

// Expose Program class for integration tests
public partial class Program;
