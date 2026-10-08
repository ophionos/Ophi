using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Ophi.Api.Features.Settings;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Push;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Settings;

/// <summary>Telegram / Pushover / ntfy across GetSettings, UpdateSettings and the test-send slice (B-2).</summary>
public class PushChannelSettingsTests : IDisposable
{
    private const string ValidUserKey = "uQiRzpo4DXghDmr9QzzfQu27cmVRsG";

    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<ITelegramService> _telegram = new();
    private readonly Mock<IPushoverService> _pushover = new();
    private readonly Mock<INtfyService> _ntfy = new();
    private readonly Guid _userId = Guid.NewGuid();

    public PushChannelSettingsTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContext.Users.Add(TestEntityFactory.User(_userId).Build());
        _dbContext.SaveChanges();
    }

    private GetSettings.Handler GetHandler() => new(
        _dbContext, Options.Create(new EmailSettings()), _telegram.Object, _pushover.Object,
        NullLogger<GetSettings.Handler>.Instance);

    private User User() => _dbContext.Users.First(u => u.Id == _userId);

    #region GetSettings

    [Fact]
    public async Task Get_ReportsAvailabilityFromOperatorConfig_AndBotUsername()
    {
        _telegram.SetupGet(t => t.IsConfigured).Returns(true);
        _telegram.SetupGet(t => t.BotUsername).Returns("OphiAlertsBot");
        _pushover.SetupGet(p => p.IsConfigured).Returns(false);

        var result = await GetHandler().Handle(new GetSettings.Query { UserId = _userId }, TestContext.Current.CancellationToken);

        result.TelegramAvailable.Should().BeTrue();
        result.TelegramBotUsername.Should().Be("OphiAlertsBot");
        result.PushoverAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Get_ReportsRecipientsAsBooleansOnly()
    {
        User().TelegramChatId = "42";
        User().TelegramNotificationsEnabled = true;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await GetHandler().Handle(new GetSettings.Query { UserId = _userId }, TestContext.Current.CancellationToken);

        result.TelegramConfigured.Should().BeTrue();
        result.TelegramNotificationsEnabled.Should().BeTrue();
        result.PushoverConfigured.Should().BeFalse();
        result.NtfyConfigured.Should().BeFalse();
        // The chat id / user key / topic URL are never echoed — the response has no field that carries them.
        typeof(GetSettings.Response).GetProperties().Select(p => p.Name)
            .Should().NotContain(["TelegramChatId", "PushoverUserKey", "NtfyTopicUrl"]);
    }

    #endregion

    #region UpdateSettings

    private static UpdateSettings.Command Cmd(
        string? chatId = null, bool? telegramOn = null, string? userKey = null, bool? pushoverOn = null,
        string? ntfyUrl = null, bool? ntfyOn = null) =>
        new(null, null, TelegramChatId: chatId, TelegramNotificationsEnabled: telegramOn,
            PushoverUserKey: userKey, PushoverNotificationsEnabled: pushoverOn,
            NtfyTopicUrl: ntfyUrl, NtfyNotificationsEnabled: ntfyOn);

    [Fact]
    public async Task Update_SetsAndClearsTheNtfyTopicUrl()
    {
        var handler = new UpdateSettings.Handler(_dbContext, NullLogger<UpdateSettings.Handler>.Instance);

        var set = await handler.Handle(Cmd(ntfyUrl: " https://ntfy.sh/ophi-alerts ", ntfyOn: true) with { UserId = _userId },
            TestContext.Current.CancellationToken);
        set.NtfyConfigured.Should().BeTrue();
        set.NtfyNotificationsEnabled.Should().BeTrue();
        User().NtfyTopicUrl.Should().Be("https://ntfy.sh/ophi-alerts");

        var cleared = await handler.Handle(Cmd(ntfyUrl: "") with { UserId = _userId }, TestContext.Current.CancellationToken);
        cleared.NtfyConfigured.Should().BeFalse();
        User().NtfyTopicUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("https://ntfy.sh/ophi-alerts")]
    [InlineData("")]
    public void Validator_AcceptsNtfyTopicUrls(string url) =>
        new UpdateSettings.Validator().TestValidate(Cmd(ntfyUrl: url)).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("https://ntfy.sh/")]
    [InlineData("https://ntfy.sh/ophi-alerts?auth=x")]
    [InlineData("http://192.168.1.10/ophi-alerts")] // private, and no allowlist
    [InlineData("http://localhost/ophi-alerts")]
    public void Validator_RejectsBadNtfyTopicUrls(string url) =>
        new UpdateSettings.Validator().TestValidate(Cmd(ntfyUrl: url)).ShouldHaveValidationErrorFor(x => x.NtfyTopicUrl);

    [Fact]
    public void Validator_AcceptsAnNtfyServerOnAnAllowedNetwork()
    {
        // A self-hosted ntfy on the LAN is reachable through the webhook allowlist, like a webhook.
        var validator = new UpdateSettings.Validator(Ophi.Infrastructure.Net.WebhookAddressPolicy.FromConfiguration("192.168.1.0/24"));

        validator.TestValidate(Cmd(ntfyUrl: "http://192.168.1.10/ophi-alerts")).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Update_SetsAndClearsRecipients()
    {
        var handler = new UpdateSettings.Handler(_dbContext, NullLogger<UpdateSettings.Handler>.Instance);

        var set = await handler.Handle(Cmd("42", true, ValidUserKey, true) with { UserId = _userId },
            TestContext.Current.CancellationToken);
        set.TelegramConfigured.Should().BeTrue();
        set.PushoverNotificationsEnabled.Should().BeTrue();
        User().PushoverUserKey.Should().Be(ValidUserKey);

        var cleared = await handler.Handle(Cmd(chatId: "", userKey: "") with { UserId = _userId },
            TestContext.Current.CancellationToken);
        cleared.TelegramConfigured.Should().BeFalse();
        cleared.PushoverConfigured.Should().BeFalse();
        User().TelegramChatId.Should().BeNull();
    }

    [Theory]
    [InlineData("42")]
    [InlineData("-1001234567890")]
    [InlineData("@ophi_alerts")]
    public void Validator_AcceptsTelegramChatIds(string chatId) =>
        new UpdateSettings.Validator().TestValidate(Cmd(chatId)).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("abc")]
    [InlineData("12 34")]
    [InlineData("@x")]
    public void Validator_RejectsMalformedTelegramChatIds(string chatId) =>
        new UpdateSettings.Validator().TestValidate(Cmd(chatId)).ShouldHaveValidationErrorFor(x => x.TelegramChatId);

    [Fact]
    public void Validator_AcceptsA30CharPushoverKey() =>
        new UpdateSettings.Validator().TestValidate(Cmd(userKey: ValidUserKey)).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("short")]
    [InlineData("uQiRzpo4DXghDmr9QzzfQu27cmVRs!")]
    public void Validator_RejectsMalformedPushoverKeys(string key) =>
        new UpdateSettings.Validator().TestValidate(Cmd(userKey: key)).ShouldHaveValidationErrorFor(x => x.PushoverUserKey);

    [Fact]
    public void Validator_AllowsEmptyToClear() =>
        new UpdateSettings.Validator().TestValidate(Cmd("", userKey: "")).ShouldNotHaveAnyValidationErrors();

    #endregion

    #region TestPushChannel

    private TestPushChannel.Handler TestHandler() => new(
        _dbContext, _telegram.Object, _pushover.Object, _ntfy.Object, NullLogger<TestPushChannel.Handler>.Instance);

    [Fact]
    public async Task Test_Ntfy_SendsToTheStoredTopicUrl_WithNoOperatorSetup()
    {
        User().NtfyTopicUrl = "https://ntfy.sh/ophi-alerts";
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await TestHandler().Handle(
            new TestPushChannel.Command(PushChannel.Ntfy) { UserId = _userId }, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        _ntfy.Verify(n => n.SendPriceAlertAsync(It.IsAny<PushPriceAlert>(), "https://ntfy.sh/ophi-alerts", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Test_Ntfy_WithoutTopicUrl_ExplainsWhatIsMissing()
    {
        var result = await TestHandler().Handle(
            new TestPushChannel.Command(PushChannel.Ntfy) { UserId = _userId }, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("topic URL");
    }

    [Fact]
    public async Task Test_WithoutRecipient_ExplainsWhatIsMissing()
    {
        _telegram.SetupGet(t => t.IsConfigured).Returns(true);

        var result = await TestHandler().Handle(
            new TestPushChannel.Command(PushChannel.Telegram) { UserId = _userId }, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("chat id");
    }

    [Fact]
    public async Task Test_WhenServerChannelUnavailable_SaysSo()
    {
        User().PushoverUserKey = ValidUserKey;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await TestHandler().Handle(
            new TestPushChannel.Command(PushChannel.Pushover) { UserId = _userId }, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not set up on this server");
        _pushover.Verify(p => p.SendPriceAlertAsync(It.IsAny<PushPriceAlert>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Test_SendsToTheStoredRecipient_AndReportsProviderErrors()
    {
        _telegram.SetupGet(t => t.IsConfigured).Returns(true);
        User().TelegramChatId = "42";
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ok = await TestHandler().Handle(
            new TestPushChannel.Command(PushChannel.Telegram) { UserId = _userId }, TestContext.Current.CancellationToken);
        ok.Success.Should().BeTrue();
        _telegram.Verify(t => t.SendPriceAlertAsync(It.IsAny<PushPriceAlert>(), "42", It.IsAny<CancellationToken>()), Times.Once);

        _telegram.Setup(t => t.SendPriceAlertAsync(It.IsAny<PushPriceAlert>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Forbidden"));
        var failed = await TestHandler().Handle(
            new TestPushChannel.Command(PushChannel.Telegram) { UserId = _userId }, TestContext.Current.CancellationToken);
        failed.Success.Should().BeFalse();
        failed.Error.Should().Contain("Telegram");
    }

    #endregion

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
