using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Ophi.Infrastructure;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Ophi.Worker.Configuration;
using Ophi.Worker.Services;
using Ophi.Worker.Settings;
using Wolverine;

var builder = Host.CreateApplicationBuilder(args);

// Add Infrastructure services
builder.Services.AddInfrastructure(builder.Configuration);

// Add Settings
builder.Services.Configure<AlertSettings>(
    builder.Configuration.GetSection(AlertSettings.SectionName));
builder.Services.Configure<WorkerSettings>(
    builder.Configuration.GetSection(WorkerSettings.SectionName));

// Configure Wolverine — standalone worker: listens on the Postgres transport queue and runs handlers.
builder.Services.AddWolverine(opts =>
{
    var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? builder.Configuration["POSTGRES_CONNECTION"];
    WolverineConfig.Configure(opts, WolverineMode.SplitWorker, postgresConnectionString);
});

// TimeProvider — single abstraction for "what time is it" so tests can fake it.
builder.Services.AddSingleton(TimeProvider.System);

// Add services
builder.Services.AddSingleton<DataRetentionService>();
builder.Services.AddSingleton<ExchangeRateRefresher>();
builder.Services.AddHostedService<PriceCheckDispatcher>();

var host = builder.Build();

// Ensure database is created (not under `codegen`, which only builds the host to generate handler code)
if (!DynamicCodeBuilder.WithinCodegenCommand)
{
    using var scope = host.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
    dbContext.Database.Migrate();
}

// `codegen write` (scripts/wolverine-codegen.sh) goes through JasperFx; JasperFx parses every argument as a command.
if (DynamicCodeBuilder.WithinCodegenCommand)
{
    return await host.RunJasperFxCommands(args);
}

host.Run();
return 0;
