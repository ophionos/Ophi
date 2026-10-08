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

    #region ntfy

    private NtfyService Ntfy() => new(new HttpClient(_http.Object), NullLogger<NtfyService>.Instance);

    [Theory]
    [InlineData("https://ntfy.sh/ophi-alerts", "https://ntfy.sh/", "ophi-alerts")]
    [InlineData("http://ntfy.lan:8080/ophi-alerts", "http://ntfy.lan:8080/", "ophi-alerts")]
    [InlineData("https://example.com/ntfy/ophi-alerts", "https://example.com/ntfy/", "ophi-alerts")]
    public async Task Ntfy_PublishesJsonToTheServerRootWithTheTopic(string topicUrl, string expectedPostUrl, string expectedTopic)
    {
        // JSON publishing, not headers: a product name can hold any character, and header values
        // cannot carry most of them.
        Respond(HttpStatusCode.OK);

        await Ntfy().SendPriceAlertAsync(Alert, topicUrl, TestContext.Current.CancellationToken);

        _sent!.Method.Should().Be(HttpMethod.Post);
        _sent.RequestUri!.ToString().Should().Be(expectedPostUrl);
        using var json = JsonDocument.Parse(_sentBody!);
        json.RootElement.GetProperty("topic").GetString().Should().Be(expectedTopic);
        json.RootElement.GetProperty("title").GetString().Should().Be(PushMessage.Title(Alert));
        json.RootElement.GetProperty("message").GetString().Should().Be(PushMessage.Body(Alert));
        json.RootElement.GetProperty("click").GetString().Should().Be("https://example.com/w");
        json.RootElement.TryGetProperty("markdown", out _).Should().BeFalse("plain text keeps a product name from injecting markup");
    }

    [Fact]
    public async Task Ntfy_OnErrorStatus_Throws_SoTheHandlerRetries()
    {
        Respond(HttpStatusCode.Forbidden);

        var act = () => Ntfy().SendPriceAlertAsync(Alert, "https://ntfy.sh/ophi-alerts", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData("https://ntfy.sh/ophi-alerts", true)]
    [InlineData("https://ntfy.sh/ophi_alerts-2", true)]
    [InlineData("https://example.com/ntfy/ophi-alerts", true)]
    [InlineData("https://ntfy.sh/", false)]
    [InlineData("https://ntfy.sh", false)]
    [InlineData("https://ntfy.sh/ophi-alerts?auth=x", false)]
    [InlineData("https://ntfy.sh/ophi-alerts#x", false)]
    [InlineData("https://ntfy.sh/bad topic", false)]
    [InlineData("https://ntfy.sh/ophi-alerts/json", false)] // a subscribe endpoint, not a topic
    [InlineData("ftp://ntfy.sh/ophi-alerts", false)]
    [InlineData("not a url", false)]
    public void Ntfy_IsValidTopicUrl(string url, bool expected) =>
        NtfyService.IsValidTopicUrl(url).Should().Be(expected);

    #endregion

    [Fact]
    public void Format_PercentDropTarget_RendersAsPercentage()
    {
        var text = PushMessage.Body(Alert with { TargetPrice = 20m, Condition = AlertCondition.PercentDrop });
        text.Should().Contain("20%").And.NotContain("USD 20.00");
    }
}
