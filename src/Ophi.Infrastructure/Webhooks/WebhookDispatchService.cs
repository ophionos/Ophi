using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ophi.Infrastructure.Metrics;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Infrastructure.Webhooks;

public class WebhookDispatchService(
    OphiDbContext dbContext,
    HttpClient httpClient,
    TimeProvider timeProvider,
    ILogger<WebhookDispatchService> logger) : IWebhookDispatchService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task DispatchAsync(string eventType, Guid userId, WebhookPayload payload, CancellationToken ct = default)
    {
        var targets = await dbContext.WebhookTargets
            .Where(t => t.UserId == userId && t.IsEnabled)
            .ToListAsync(ct);

        var matching = targets.Where(t => t.Events.Contains(eventType, StringComparer.OrdinalIgnoreCase)).ToList();

        await Task.WhenAll(matching.Select(target => SafePostAsync(target.Url, target.Name, target.Id, eventType, payload, ct)));
    }

    public async Task<(bool Success, string? Error)> SendTestAsync(string url, CancellationToken ct = default)
    {
        var payload = new WebhookPayload(
            ProductId: Guid.Empty,
            ProductName: "Test Product",
            ProductUrl: "https://example.com/product",
            OldPrice: 99.99m,
            NewPrice: 79.99m,
            Currency: "USD",
            Timestamp: timeProvider.GetUtcNow().UtcDateTime
        );

        try
        {
            await PostAsync(url, "test", payload, ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task SafePostAsync(string url, string targetName, Guid targetId, string eventType, WebhookPayload payload, CancellationToken ct)
    {
        try
        {
            await PostAsync(url, eventType, payload, ct);
            AppMetrics.WebhookDispatchTotal.WithLabels(eventType, "success").Inc();
            logger.LogDebug("Webhook dispatched: {TargetName} ({TargetId}) event={Event}", targetName, targetId, eventType);
        }
        catch (Exception ex)
        {
            AppMetrics.WebhookDispatchTotal.WithLabels(eventType, "failure").Inc();
            logger.LogWarning(ex, "Webhook dispatch failed for target {TargetName} ({TargetId})", targetName, targetId);
        }
    }

    private async Task PostAsync(string url, string eventType, WebhookPayload payload, CancellationToken ct)
    {
        var body = new
        {
            Event = eventType,
            ProductId = payload.ProductId,
            ProductName = payload.ProductName,
            Url = payload.ProductUrl,
            OldPrice = payload.OldPrice,
            NewPrice = payload.NewPrice,
            Currency = payload.Currency,
            Timestamp = payload.Timestamp
        };

        var json = JsonSerializer.Serialize(body, SerializerOptions);
        Exception? lastException = null;

        for (var attempt = 0; attempt <= 2; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct); // 1s, then 2s
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(url, content, cts.Token);

                // 4xx client errors = don't retry (misconfiguration, not transient), but still report failure
                if ((int)response.StatusCode is >= 400 and < 500)
                    throw new HttpRequestException($"Webhook target returned HTTP {(int)response.StatusCode}");

                if (response.IsSuccessStatusCode)
                    return;

                // 5xx server error = transient, retry
                lastException = new HttpRequestException($"Webhook target returned HTTP {(int)response.StatusCode}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw lastException!;
    }
}
