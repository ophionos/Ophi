using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Infrastructure.Webhooks;
using Ophi.TestHelpers;

namespace Ophi.Infrastructure.Tests.Webhooks;

/// <summary>
/// The webhook test posts to a caller-chosen URL. Its error must not carry the raw failure text:
/// "connection refused" vs "timeout" vs an HTTP status maps which internal hosts and ports exist.
/// </summary>
public class WebhookDispatchServiceTestTests
{
    public static TheoryData<Func<HttpResponseMessage>> Failures => new()
    {
        () => throw new HttpRequestException("No connection could be made (internal-host:8080)"),
        () => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
        () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task SendTestAsync_OnAnyFailure_ReturnsTheGenericError(Func<HttpResponseMessage> respond)
    {
        var (db, connection) = TestDbContextFactory.Create();
        using var _ = connection;
        using var __ = db;
        using var http = new HttpClient(new StubHandler(respond));
        var service = new WebhookDispatchService(db, http, TimeProvider.System, NullLogger<WebhookDispatchService>.Instance);

        var (success, error) = await service.SendTestAsync("https://hooks.example.com/x", TestContext.Current.CancellationToken);

        success.Should().BeFalse();
        error.Should().Be(WebhookDispatchService.TestFailedMessage);
    }

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }
}
