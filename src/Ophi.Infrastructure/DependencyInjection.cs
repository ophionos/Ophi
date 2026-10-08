using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Infrastructure.Discord;
using Ophi.Infrastructure.Push;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using Ophi.Infrastructure.Webhooks;

namespace Ophi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database provider — DB_PROVIDER selects postgres (default) or sqlite.
        // We standardized on Postgres for ALL real deployments; migrations
        // are Postgres-only. SQLite is now test-only: the unit/integration test harnesses build
        // their own SQLite DbContext (UseSqlite + EnsureCreated, never Migrate) and don't go
        // through this default. Selecting sqlite at runtime is therefore only for those harnesses.
        var provider = (configuration["DB_PROVIDER"] ?? "postgres").Trim().ToLowerInvariant();
        if (provider == "postgres")
        {
            // Connection string is resolved lazily inside the options lambda (first DbContext
            // resolution), so merely calling AddInfrastructure without DB config never throws —
            // only an actual production resolve (e.g. startup Migrate()) fails fast when unset.
            services.AddDbContext<OphiDbContext>(options =>
            {
                var connectionString = configuration.GetConnectionString("Postgres")
                    ?? configuration["POSTGRES_CONNECTION"]
                    ?? throw new InvalidOperationException(
                        "DB_PROVIDER=postgres (the default) requires a connection string in " +
                        "ConnectionStrings:Postgres (or POSTGRES_CONNECTION).");

                options.UseNpgsql(connectionString,
                    npgsqlOptions => npgsqlOptions.CommandTimeout(30));
            });
        }
        else
        {
            // SQLite — ensure the directory exists before SQLite tries to open the file
            var databasePath = configuration["DATABASE_PATH"] ?? "./data/ophi.db";
            var dbDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (!string.IsNullOrEmpty(dbDirectory))
            {
                Directory.CreateDirectory(dbDirectory);
            }

            services.AddDbContext<OphiDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}",
                    sqliteOptions => sqliteOptions.CommandTimeout(30)));

            // Enable WAL mode for concurrent read/write (API + Worker share the DB). SQLite-only.
            using var walConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}");
            walConnection.Open();
            using var walCommand = walConnection.CreateCommand();
            walCommand.CommandText = "PRAGMA journal_mode=WAL;";
            walCommand.ExecuteNonQuery();
        }
        // Scraping - store config providers
        services.AddSingleton<CodeStoreConfigProvider>();
        services.AddScoped<IStoreConfigProvider, CombinedStoreConfigProvider>();

        // Memory cache for store config caching
        services.AddMemoryCache();

        // Scraping services
        // HTTP-based scraping service (fast, works for most sites)
        services.AddHttpClient<ScrapingService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Register HTTP scraping service as keyed service
        services.AddKeyedScoped<IScrapingService, ScrapingService>("http",
            (sp, _) => sp.GetRequiredService<ScrapingService>());

        // Playwright — disabled via DISABLE_PLAYWRIGHT for lightweight deployments
        var disablePlaywright = configuration.GetValue<bool>("DISABLE_PLAYWRIGHT");
        if (disablePlaywright)
        {
            services.AddKeyedSingleton<IScrapingService, NoOpPlaywrightScrapingService>("playwright");
        }
        else
        {
            services.AddSingleton<IPlaywrightBrowserManager, PlaywrightBrowserManager>();
            services.AddScoped<PlaywrightScrapingService>();
            services.AddKeyedScoped<IScrapingService, PlaywrightScrapingService>("playwright",
                (sp, _) => sp.GetRequiredService<PlaywrightScrapingService>());
        }

        // Hybrid service that tries HTTP first, falls back to Playwright
        services.AddScoped<IScrapingService, HybridScrapingService>();

        // Auto store creation service
        services.AddScoped<IAutoCreateStoreService, AutoCreateStoreService>();

        // Email — all four fields must be present together, or none (PORT has a safe default)
        if (!string.IsNullOrEmpty(configuration["SMTP_HOST"]))
        {
            string[] required = ["SMTP_HOST", "SMTP_USER", "SMTP_PASS", "SMTP_FROM"];
            var missing = required.Where(k => string.IsNullOrEmpty(configuration[k])).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"SMTP is partially configured. Missing: {string.Join(", ", missing)}. " +
                    "Configure all of SMTP_HOST, SMTP_USER, SMTP_PASS, SMTP_FROM, or leave SMTP_HOST empty to disable email.");
            }
        }

        services.Configure<EmailSettings>(options =>
        {
            options.SmtpHost = configuration["SMTP_HOST"] ?? "";
            options.SmtpPort = int.TryParse(configuration["SMTP_PORT"], out var port) ? port : 587;
            options.SmtpUser = configuration["SMTP_USER"] ?? "";
            options.SmtpPass = configuration["SMTP_PASS"] ?? "";
            options.FromEmail = configuration["SMTP_FROM"] ?? "";
            options.FromName = "Ophi";
            options.AppUrl = configuration["APP_URL"] ?? "http://localhost:5173";
        });
        services.AddSingleton<ISmtpClientFactory, SmtpClientFactory>();
        services.AddScoped<IEmailService, EmailService>();

        // Discord (each user's own webhook URL; there is no operator-wide one)
        // RemoveAllLoggers: the webhook URL is the credential, and the default HttpClient logging
        // handlers write the full request URI at Information (only the query string is redacted).
        // Register the interface as the typed client: a separate AddScoped<IDiscordService, ...>
        // resolves the default HttpClient, which drops both the timeout and RemoveAllLoggers.
        services.AddHttpClient<IDiscordService, DiscordWebhookService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        }).RemoveAllLoggers();

        // Push channels — one operator bot / app; users store only their recipient.
        services.Configure<TelegramSettings>(options =>
        {
            options.BotToken = configuration["TELEGRAM_BOT_TOKEN"] ?? "";
            options.BotUsername = configuration["TELEGRAM_BOT_USERNAME"] ?? "";
        });
        // The bot token is in the URL path (/bot{token}/…) — same reason as Discord above.
        services.AddHttpClient<ITelegramService, TelegramService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        }).RemoveAllLoggers();
        services.Configure<PushoverSettings>(options =>
        {
            options.AppToken = configuration["PUSHOVER_APP_TOKEN"] ?? "";
        });
        services.AddHttpClient<IPushoverService, PushoverService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Display-currency rates (ECB). Only the worker's ExchangeRateRefresher calls this.
        services.AddHttpClient<Ophi.Infrastructure.Fx.EcbRatesClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // Generic outbound webhooks — user target URLs often embed a secret (Slack, ntfy, Home
        // Assistant), so no URI logging here either. Interface-typed for the same reason as Discord.
        services.AddHttpClient<IWebhookDispatchService, WebhookDispatchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        }).RemoveAllLoggers();

        return services;
    }
}
