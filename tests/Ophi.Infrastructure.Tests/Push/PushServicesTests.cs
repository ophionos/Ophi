using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Push;

namespace Ophi.Infrastructure.Tests.Push;

public class PushServicesTests
{
    private readonly Mock<HttpMessageHandler> _http = new();
    private HttpRequestMessage? _sent;
    private string? _sentBody;

    private static readonly PushPriceAlert Alert = new(
        "Widget *bold* <b>x</b>", "https://example.com/w", 45m, 50m, "USD", AlertCondition.Below);

    private void Respond(HttpStatusCode status)
    {
        _http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, ct) =>
            {
                _sent = req;
                _sentBody = req.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            })
            .ReturnsAsync(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }

    private void VerifyNothingSent() =>
        _http.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());

    #region Telegram

    private TelegramService Telegram(string token = "123:abc") =>
        new(new HttpClient(_http.Object), Options.Create(new TelegramSettings { BotToken = token }),
            NullLogger<TelegramService>.Instance);

    [Fact]
    public void Telegram_IsConfigured_OnlyWithBotToken()
    {
        Telegram("").IsConfigured.Should().BeFalse();
        Telegram("123:abc").IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task Telegram_WithoutBotToken_SendsNothing()
    {
        await Telegram("").SendPriceAlertAsync(Alert, "42", TestContext.Current.CancellationToken);
        VerifyNothingSent();
    }

    [Fact]
    public async Task Telegram_PostsPlainTextSendMessageToTheChat()
    {
        Respond(HttpStatusCode.OK);

        await Telegram().SendPriceAlertAsync(Alert, "42", TestContext.Current.CancellationToken);

        _sent!.RequestUri!.ToString().Should().Be("https://api.telegram.org/bot123:abc/sendMessage");
        using var json = JsonDocument.Parse(_sentBody!);
        json.RootElement.GetProperty("chat_id").GetString().Should().Be("42");
        json.RootElement.TryGetProperty("parse_mode", out _).Should().BeFalse(
            "plain text keeps a product name from injecting formatting");
        var text = json.RootElement.GetProperty("text").GetString();
        text.Should().Contain("Widget *bold* <b>x</b>").And.Contain("USD 45.00").And.Contain("https://example.com/w");
    }

    [Fact]
    public async Task Telegram_OnErrorStatus_Throws_SoTheHandlerRetries()
    {
        Respond(HttpStatusCode.Forbidden);

        var act = () => Telegram().SendPriceAlertAsync(Alert, "42", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region Pushover

    private PushoverService Pushover(string token = "apptoken") =>
        new(new HttpClient(_http.Object), Options.Create(new PushoverSettings { AppToken = token }),
            NullLogger<PushoverService>.Instance);

    [Fact]
    public void Pushover_IsConfigured_OnlyWithAppToken()
    {
        Pushover("").IsConfigured.Should().BeFalse();
        Pushover("apptoken").IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task Pushover_WithoutAppToken_SendsNothing()
    {
        await Pushover("").SendPriceAlertAsync(Alert, "userkey", TestContext.Current.CancellationToken);
        VerifyNothingSent();
    }

    [Fact]
    public async Task Pushover_PostsFormWithAppTokenUserKeyAndPlainMessage()
    {
        Respond(HttpStatusCode.OK);

        await Pushover().SendPriceAlertAsync(Alert, "userkey", TestContext.Current.CancellationToken);

        _sent!.RequestUri!.ToString().Should().Be("https://api.pushover.net/1/messages.json");
        var form = System.Web.HttpUtility.ParseQueryString(_sentBody!);
        form["token"].Should().Be("apptoken");
        form["user"].Should().Be("userkey");
        form["title"].Should().Contain("Widget");
        form["message"].Should().Contain("USD 45.00");
        form["url"].Should().Be("https://example.com/w");
        form["html"].Should().BeNull("plain text keeps a product name from injecting markup");
    }

    [Fact]
    public async Task Pushover_OnErrorStatus_Throws_SoTheHandlerRetries()
    {
        Respond(HttpStatusCode.BadRequest);

        var act = () => Pushover().SendPriceAlertAsync(Alert, "userkey", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    [Fact]
    public void Format_PercentDropTarget_RendersAsPercentage()
    {
        var text = PushMessage.Body(Alert with { TargetPrice = 20m, Condition = AlertCondition.PercentDrop });
        text.Should().Contain("20%").And.NotContain("USD 20.00");
    }
}
