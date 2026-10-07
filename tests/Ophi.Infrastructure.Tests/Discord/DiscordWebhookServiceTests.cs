using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Discord;

namespace Ophi.Infrastructure.Tests.Discord;

public class DiscordWebhookServiceTests
{
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<DiscordWebhookService>> _loggerMock;

    public DiscordWebhookServiceTests()
    {
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpHandlerMock.Object);
        _loggerMock = new Mock<ILogger<DiscordWebhookService>>();
    }

    private DiscordWebhookService CreateService(string webhookUrl = "")
    {
        var settings = Options.Create(new DiscordSettings { WebhookUrl = webhookUrl });
        return new DiscordWebhookService(_httpClient, settings, TimeProvider.System, _loggerMock.Object);
    }

    [Fact]
    public void IsConfigured_WhenWebhookUrlEmpty_ReturnsFalse()
    {
        var service = CreateService();
        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_WhenWebhookUrlSet_ReturnsTrue()
    {
        var service = CreateService("https://discord.com/api/webhooks/123/abc");
        service.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_WhenWebhookUrlWhitespace_ReturnsFalse()
    {
        var service = CreateService("   ");
        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task SendPriceAlertAsync_WhenNotConfigured_DoesNotSendRequest()
    {
        var service = CreateService();
        var alert = new DiscordPriceAlert("Widget", "https://example.com", 45m, 50m, "USD");

        await service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendPriceAlertAsync_WhenConfigured_PostsToWebhookUrl()
    {
        var webhookUrl = "https://discord.com/api/webhooks/123/abc";
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var service = CreateService(webhookUrl);
        var alert = new DiscordPriceAlert("Widget", "https://example.com/widget", 45m, 50m, "USD");

        await service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString() == webhookUrl),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendPriceAlertAsync_PayloadContainsProductDetails()
    {
        string? capturedBody = null;
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, t) =>
            {
                capturedBody = req.Content!.ReadAsStringAsync(t).GetAwaiter().GetResult();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var service = CreateService("https://discord.com/api/webhooks/123/abc");
        var alert = new DiscordPriceAlert("Super Widget", "https://example.com/widget", 45.99m, 50.00m, "EUR");

        await service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("Super Widget");
        capturedBody.Should().Contain("EUR 45.99");
        capturedBody.Should().Contain("EUR 50.00");
        capturedBody.Should().Contain("https://example.com/widget");
        capturedBody.Should().Contain("embeds");
    }

    [Fact]
    public async Task SendPriceAlertAsync_ForPercentDrop_RendersTheTargetAsAPercentage()
    {
        // TargetPrice is a percentage for this condition. The embed used to render it as money, so a
        // "20% drop" alert reached Discord as "USD 20.00" — a plausible-looking price the user never set.
        string? capturedBody = null;
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, t) =>
            {
                capturedBody = req.Content!.ReadAsStringAsync(t).GetAwaiter().GetResult();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var service = CreateService("https://discord.com/api/webhooks/123/abc");
        var alert = new DiscordPriceAlert(
            "Super Widget", "https://example.com/widget", 45.99m, 20m, "USD", AlertCondition.PercentDrop);

        await service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("20%");
        capturedBody.Should().NotContain("USD 20.00");
        capturedBody.Should().Contain("USD 45.99", "the current price is still a real amount");
    }

    [Fact]
    public async Task SendPriceAlertAsync_ForBelow_StillRendersTheTargetAsMoney()
    {
        string? capturedBody = null;
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, t) =>
            {
                capturedBody = req.Content!.ReadAsStringAsync(t).GetAwaiter().GetResult();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var service = CreateService("https://discord.com/api/webhooks/123/abc");
        var alert = new DiscordPriceAlert(
            "Super Widget", "https://example.com/widget", 45.99m, 50m, "USD", AlertCondition.Below);

        await service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        capturedBody.Should().Contain("USD 50.00");
        capturedBody.Should().NotContain("50%");
    }

    [Fact]
    public async Task SendPriceAlertAsync_WhenHttpFails_ThrowsException()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var service = CreateService("https://discord.com/api/webhooks/123/abc");
        var alert = new DiscordPriceAlert("Widget", "https://example.com", 45m, 50m, "USD");

        var act = () => service.SendPriceAlertAsync(alert);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SendPriceAlertAsync_SetsJsonContentType()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var service = CreateService("https://discord.com/api/webhooks/123/abc");
        var alert = new DiscordPriceAlert("Widget", "https://example.com", 45m, 50m, "USD");

        await service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Content!.Headers.ContentType!.MediaType == "application/json"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendPriceAlertAsync_WithExplicitUrl_PostsToThatUrl()
    {
        var explicitUrl = "https://discord.com/api/webhooks/456/xyz";
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var service = CreateService(); // No global config
        var alert = new DiscordPriceAlert("Widget", "https://example.com", 45m, 50m, "USD");

        await service.SendPriceAlertAsync(alert, explicitUrl, TestContext.Current.CancellationToken);

        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString() == explicitUrl),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendPriceAlertAsync_WithNullExplicitUrl_SkipsSend()
    {
        var service = CreateService();
        var alert = new DiscordPriceAlert("Widget", "https://example.com", 45m, 50m, "USD");

        await service.SendPriceAlertAsync(alert, null!, TestContext.Current.CancellationToken);

        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendPriceAlertAsync_WithEmptyExplicitUrl_SkipsSend()
    {
        var service = CreateService();
        var alert = new DiscordPriceAlert("Widget", "https://example.com", 45m, 50m, "USD");

        await service.SendPriceAlertAsync(alert, "", TestContext.Current.CancellationToken);

        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    // --- Failure-mode coverage (Phase 6.7) ---
    // Previously only the InternalServerError happy-path failure was covered.

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task SendPriceAlertAsync_WithClientError_ThrowsHttpRequestException(HttpStatusCode status)
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status));

        var service = CreateService("https://discord.com/api/webhooks/123/abc");
        var alert = new DiscordPriceAlert("Widget", "https://example.com", 45m, 50m, "USD");

        var act = () => service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>(
            $"Discord {(int)status} should surface to the caller, not be silently swallowed");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task SendPriceAlertAsync_WithServerError_ThrowsHttpRequestException(HttpStatusCode status)
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status));

        var service = CreateService("https://discord.com/api/webhooks/123/abc");
        var alert = new DiscordPriceAlert("Widget", "https://example.com", 45m, 50m, "USD");

        var act = () => service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>(
            "5xx must propagate so Wolverine retry/error policies can react");
    }

    [Fact]
    public async Task SendPriceAlertAsync_WhenNetworkExceptionThrown_PropagatesException()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var service = CreateService("https://discord.com/api/webhooks/123/abc");
        var alert = new DiscordPriceAlert("Widget", "https://example.com", 45m, 50m, "USD");

        var act = () => service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<HttpRequestException>();
        thrown.Which.Message.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task SendPriceAlertAsync_WhenTimedOut_PropagatesTaskCanceledException()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Request timed out"));

        var service = CreateService("https://discord.com/api/webhooks/123/abc");
        var alert = new DiscordPriceAlert("Widget", "https://example.com", 45m, 50m, "USD");

        var act = () => service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TaskCanceledException>(
            "timeouts must propagate so Wolverine can retry the message");
    }

    [Fact]
    public async Task SendPriceAlertAsync_WithExplicitUrl_OnClientError_ThrowsHttpRequestException()
    {
        // Same failure modes apply to the per-user-webhook overload.
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var service = CreateService(); // no global config
        var alert = new DiscordPriceAlert("Widget", "https://example.com", 45m, 50m, "USD");
        const string explicitUrl = "https://discord.com/api/webhooks/456/xyz";

        var act = () => service.SendPriceAlertAsync(alert, explicitUrl, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
