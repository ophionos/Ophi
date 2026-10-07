using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ophi.Api.Common.HealthChecks;

public static class HealthCheckResponseWriter
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    private static readonly string Version =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        // StartTime is captured at process start (static init, before DI), so uptime always uses
        // wall-clock UtcNow. Tests that fake time still get a stable diagnostic timestamp from
        // the DI-resolved TimeProvider below.
        var timeProvider = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var uptime = now - StartTime;
        var response = new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString().ToLowerInvariant(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds,
                    data = e.Value.Data.Count > 0 ? e.Value.Data : null
                }),
            uptime = FormatUptime(uptime),
            version = Version,
            timestamp = now
        };

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(response, JsonOptions));
    }

    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
    }
}
