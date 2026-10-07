using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Tests.Integration;

/// <summary>
/// Exercises the live SSE path end-to-end in-process: <c>GET /api/v1/events</c> → connection registry
/// → <c>PushLiveUpdateHandler</c>. The unit tests cover the registry and handler in isolation; this
/// proves the wire actually carries a frame when a <see cref="LiveUpdate"/> is published on the bus.
/// Every read is bounded by a CancellationToken so a wiring regression fails by timeout, never hangs.
/// </summary>
public class EventsStreamEndpointTests : IsolatedIntegrationTest, IClassFixture<OphiWebApplicationFactory>
{
    public EventsStreamEndpointTests(OphiWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Get_Events_Unauthenticated_Returns401()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/events", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_Events_Authenticated_StreamsFrameWhenLiveUpdatePublished()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;

        using var response = await client.GetAsync(
            "/api/v1/events", HttpCompletionOption.ResponseHeadersRead, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        // The endpoint flushes a `: connected` preamble immediately so the client's onopen fires and
        // (critically here) the registry has finished registering before we publish.
        var preamble = await reader.ReadLineAsync(ct);
        preamble.Should().Be(": connected");

        // Drive the path under test: publish on the bus exactly as the worker would.
        var productId = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(new LiveUpdate(userId, LiveUpdate.ScrapeCompleted, productId));
        }

        // Read until the data frame arrives (skipping blank lines / heartbeat comments).
        var dataLine = await ReadDataLineAsync(reader, ct);
        dataLine.Should().NotBeNull();

        using var doc = JsonDocument.Parse(dataLine!["data: ".Length..]);
        doc.RootElement.GetProperty("type").GetString().Should().Be("scrape-completed");
        doc.RootElement.GetProperty("productId").GetGuid().Should().Be(productId);

        client.Dispose();
    }

    private static async Task<string?> ReadDataLineAsync(StreamReader reader, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) return null;
            if (line.StartsWith("data: ", StringComparison.Ordinal)) return line;
        }
        return null;
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync()
    {
        var client = Factory.CreateClient();
        var email = $"events-test-{Guid.NewGuid()}@example.com";
        const string password = "Password123!";

        await client.PostAsJsonAsync("/api/v1/auth/register",
            new { Email = email, Password = password, Name = "Test User" }, TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync("/api/v1/auth/login",
            new { Email = email, Password = password }, TestContext.Current.CancellationToken);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == email, TestContext.Current.CancellationToken);
        return (client, user.Id);
    }
}
