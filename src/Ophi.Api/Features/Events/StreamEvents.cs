using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Ophi.Api.Common.Events;
using Ophi.Api.Common.Extensions;

namespace Ophi.Api.Features.Events;

/// <summary>
/// <c>GET /api/v1/events</c> — a long-lived Server-Sent-Events stream the browser subscribes to once,
/// app-wide. The connection registers with <see cref="ISseConnectionRegistry"/> and drains its channel,
/// writing each <c>LiveUpdate</c> frame as an SSE <c>data:</c> event so every open surface can refetch.
///
/// <para>
/// Auth is cookie-session only: native <c>EventSource</c> can't send an <c>Authorization</c> header, so
/// Bearer API keys don't apply here (they remain valid on every other endpoint for scripting).
/// </para>
/// </summary>
public static class StreamEvents
{
    // Idle keep-alive: a comment line keeps the connection from being reaped by proxy read timeouts
    // (WSL/nginx) when no real events flow. Comments are ignored by the EventSource parser.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    public static void MapStreamEventsEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/events", async (HttpContext context, ISseConnectionRegistry registry) =>
        {
            var userId = context.User.GetUserId();

            var response = context.Response;
            response.Headers.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            // Note: no `Connection: keep-alive` header — it's a hop-by-hop header that is illegal under
            // HTTP/2 (Kestrel rejects it) and unnecessary for SSE on either protocol version.
            // Defeat reverse-proxy response buffering (nginx honours this) so frames flush immediately.
            response.Headers["X-Accel-Buffering"] = "no";
            // Disable Kestrel's own response buffering for the same reason.
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            var connection = registry.Register(userId);
            var cancellationToken = context.RequestAborted;

            try
            {
                // Open the stream immediately so the client's onopen fires (and the proxy commits headers).
                await WriteAsync(response, ": connected\n\n", cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    string? frame = null;
                    try
                    {
                        // Wake on either a new frame or the heartbeat deadline, whichever comes first.
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeoutCts.CancelAfter(HeartbeatInterval);
                        frame = await connection.Reader.ReadAsync(timeoutCts.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Heartbeat tick — no frame ready; fall through to send a keep-alive comment.
                    }

                    if (frame is not null)
                        await WriteAsync(response, $"data: {frame}\n\n", cancellationToken);
                    else
                        await WriteAsync(response, ": ping\n\n", cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected — expected; fall through to cleanup.
            }
            finally
            {
                registry.Unregister(userId, connection.Id);
            }
        })
        .WithName("StreamEvents")
        .WithTags("Events")
        .WithSummary("Subscribe to real-time updates")
        .WithDescription("Long-lived Server-Sent-Events stream of per-user change pings (scrape completed, notification created). Cookie authentication only.")
        .RequireAuthorization();

    private static async Task WriteAsync(HttpResponse response, string text, CancellationToken cancellationToken)
    {
        await response.WriteAsync(text, Encoding.UTF8, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
