using FluentAssertions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Email;

namespace Ophi.Infrastructure.Tests.Email;

public class EmailServiceTests
{
    private readonly Mock<ILogger<EmailService>> _loggerMock;
    private readonly Mock<ISmtpClient> _smtpClientMock;
    private readonly EmailService _service;

    private MimeMessage? _capturedMessage;

    public EmailServiceTests()
    {
        var settings = new EmailSettings
        {
            SmtpHost = "smtp.test.com",
            SmtpPort = 587,
            SmtpUser = "testuser",
            SmtpPass = "testpass",
            FromEmail = "noreply@ophi.app",
            FromName = "Ophi",
            AppUrl = "https://ophi.example.com"
        };

        var settingsMock = new Mock<IOptions<EmailSettings>>();
        settingsMock.Setup(x => x.Value).Returns(settings);

        _loggerMock = new Mock<ILogger<EmailService>>();

        _smtpClientMock = new Mock<ISmtpClient>();
        _smtpClientMock
            .Setup(x => x.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()))
            .Callback<MimeMessage, CancellationToken, ITransferProgress>((msg, _, _) => _capturedMessage = msg)
            .Returns(Task.FromResult("OK"));

        var smtpClientFactoryMock = new Mock<ISmtpClientFactory>();
        smtpClientFactoryMock.Setup(x => x.Create()).Returns(_smtpClientMock.Object);

        _service = new EmailService(settingsMock.Object, _loggerMock.Object, smtpClientFactoryMock.Object);
    }

    #region SendPriceAlertAsync Tests

    [Fact]
    public async Task SendPriceAlertAsync_WithValidAlert_SendsEmailWithCorrectSubject()
    {
        // Arrange
        var alert = new PriceAlertEmail(
            ToEmail: "user@example.com",
            ToName: "Test User",
            ProductName: "Sony Headphones",
            ProductUrl: "https://amazon.com/product/123",
            CurrentPrice: 99.99m,
            TargetPrice: 100.00m,
            Currency: "USD"
        );

        // Act
        await _service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        // Assert
        _capturedMessage.Should().NotBeNull();
        _capturedMessage!.Subject.Should().Contain("Price Alert: Sony Headphones dropped to USD");
        _capturedMessage.Subject.Should().Contain("99");
    }

    [Fact]
    public async Task SendPriceAlertAsync_WithValidAlert_SendsEmailWithCorrectRecipient()
    {
        // Arrange
        var alert = new PriceAlertEmail(
            ToEmail: "user@example.com",
            ToName: "Test User",
            ProductName: "Test Product",
            ProductUrl: "https://example.com/product",
            CurrentPrice: 49.99m,
            TargetPrice: 50.00m,
            Currency: "USD"
        );

        // Act
        await _service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        // Assert
        _capturedMessage.Should().NotBeNull();
        _capturedMessage!.To.Count.Should().Be(1);
        var recipient = _capturedMessage.To.First() as MailboxAddress;
        recipient!.Address.Should().Be("user@example.com");
        recipient.Name.Should().Be("Test User");
    }

    [Fact]
    public async Task SendPriceAlertAsync_WithValidAlert_SendsEmailFromConfiguredAddress()
    {
        // Arrange
        var alert = new PriceAlertEmail(
            ToEmail: "user@example.com",
            ToName: "Test User",
            ProductName: "Test Product",
            ProductUrl: "https://example.com/product",
            CurrentPrice: 49.99m,
            TargetPrice: 50.00m,
            Currency: "USD"
        );

        // Act
        await _service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        // Assert
        _capturedMessage.Should().NotBeNull();
        var sender = _capturedMessage!.From.First() as MailboxAddress;
        sender!.Address.Should().Be("noreply@ophi.app");
        sender.Name.Should().Be("Ophi");
    }

    [Fact]
    public async Task SendPriceAlertAsync_WithValidAlert_IncludesProductUrlInBody()
    {
        // Arrange
        var alert = new PriceAlertEmail(
            ToEmail: "user@example.com",
            ToName: "Test User",
            ProductName: "Test Product",
            ProductUrl: "https://example.com/product/specific-product",
            CurrentPrice: 49.99m,
            TargetPrice: 50.00m,
            Currency: "USD"
        );

        // Act
        await _service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        // Assert
        _capturedMessage.Should().NotBeNull();
        _capturedMessage!.HtmlBody.Should().Contain("https://example.com/product/specific-product");
    }

    [Fact]
    public async Task SendPriceAlertAsync_WithValidAlert_IncludesPricesInBody()
    {
        // Arrange
        var alert = new PriceAlertEmail(
            ToEmail: "user@example.com",
            ToName: "Test User",
            ProductName: "Test Product",
            ProductUrl: "https://example.com/product",
            CurrentPrice: 149.99m,
            TargetPrice: 200.00m,
            Currency: "EUR"
        );

        // Act
        await _service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        // Assert
        _capturedMessage.Should().NotBeNull();
        _capturedMessage!.HtmlBody.Should().Contain("EUR");
        _capturedMessage.HtmlBody.Should().Contain("149");
        _capturedMessage.HtmlBody.Should().Contain("200");
    }

    [Fact]
    public async Task SendPriceAlertAsync_ForPercentDrop_RendersTheTargetAsAPercentage()
    {
        // TargetPrice is a percentage for this condition, but the template rendered it as money —
        // so a "20% drop" alert emailed "Your Target: USD 20.00", which reads as a price target the
        // user never set. Nothing asserted on this line before, which is how it survived.
        var alert = new PriceAlertEmail(
            ToEmail: "user@example.com",
            ToName: "Test User",
            ProductName: "Test Product",
            ProductUrl: "https://example.com/product",
            CurrentPrice: 149.99m,
            TargetPrice: 20m,
            Currency: "USD",
            Condition: AlertCondition.PercentDrop
        );

        await _service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        _capturedMessage.Should().NotBeNull();
        _capturedMessage!.HtmlBody.Should().Contain("20%");
        _capturedMessage.HtmlBody.Should().NotContain("USD 20.00");
    }

    [Fact]
    public async Task SendPriceAlertAsync_ForBelow_StillRendersTheTargetAsMoney()
    {
        var alert = new PriceAlertEmail(
            ToEmail: "user@example.com",
            ToName: "Test User",
            ProductName: "Test Product",
            ProductUrl: "https://example.com/product",
            CurrentPrice: 149.99m,
            TargetPrice: 200.00m,
            Currency: "EUR",
            Condition: AlertCondition.Below
        );

        await _service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        _capturedMessage!.HtmlBody.Should().Contain("EUR 200.00");
        _capturedMessage.HtmlBody.Should().NotContain("200%");
    }

    [Fact]
    public async Task SendPriceAlertAsync_ConnectsToSmtpServer()
    {
        // Arrange
        var alert = new PriceAlertEmail(
            ToEmail: "user@example.com",
            ToName: "Test User",
            ProductName: "Test Product",
            ProductUrl: "https://example.com/product",
            CurrentPrice: 49.99m,
            TargetPrice: 50.00m,
            Currency: "USD"
        );

        // Act
        await _service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        // Assert
        _smtpClientMock.Verify(
            x => x.ConnectAsync("smtp.test.com", 587, SecureSocketOptions.StartTls, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendPriceAlertAsync_AuthenticatesWithCredentials()
    {
        // Arrange
        var alert = new PriceAlertEmail(
            ToEmail: "user@example.com",
            ToName: "Test User",
            ProductName: "Test Product",
            ProductUrl: "https://example.com/product",
            CurrentPrice: 49.99m,
            TargetPrice: 50.00m,
            Currency: "USD"
        );

        // Act
        await _service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        // Assert
        _smtpClientMock.Verify(
            x => x.AuthenticateAsync("testuser", "testpass", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendPriceAlertAsync_DisconnectsAfterSending()
    {
        // Arrange
        var alert = new PriceAlertEmail(
            ToEmail: "user@example.com",
            ToName: "Test User",
            ProductName: "Test Product",
            ProductUrl: "https://example.com/product",
            CurrentPrice: 49.99m,
            TargetPrice: 50.00m,
            Currency: "USD"
        );

        // Act
        await _service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);

        // Assert
        _smtpClientMock.Verify(
            x => x.DisconnectAsync(true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendPriceAlertAsync_WithSmtpError_ThrowsException()
    {
        // Arrange
        var alert = new PriceAlertEmail(
            ToEmail: "user@example.com",
            ToName: "Test User",
            ProductName: "Test Product",
            ProductUrl: "https://example.com/product",
            CurrentPrice: 49.99m,
            TargetPrice: 50.00m,
            Currency: "USD"
        );

        _smtpClientMock
            .Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SecureSocketOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection failed"));

        // Act & Assert
        await _service.Invoking(s => s.SendPriceAlertAsync(alert))
            .Should().ThrowAsync<Exception>()
            .WithMessage("Connection failed");
    }

    [Fact]
    public async Task SendPriceAlertAsync_WithSmtpError_LogsError()
    {
        // Arrange
        var alert = new PriceAlertEmail(
            ToEmail: "user@example.com",
            ToName: "Test User",
            ProductName: "Test Product",
            ProductUrl: "https://example.com/product",
            CurrentPrice: 49.99m,
            TargetPrice: 50.00m,
            Currency: "USD"
        );

        _smtpClientMock
            .Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SecureSocketOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection failed"));

        // Act
        try
        {
            await _service.SendPriceAlertAsync(alert, TestContext.Current.CancellationToken);
        }
        catch
        {
            // Expected
        }

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o!.ToString()!.Contains("user@example.com")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region SendTestEmailAsync Tests

    [Fact]
    public async Task SendTestEmailAsync_SendsToTheGivenRecipient()
    {
        await _service.SendTestEmailAsync("user@example.com", "Test User", TestContext.Current.CancellationToken);

        _capturedMessage.Should().NotBeNull();
        _capturedMessage!.To.Count.Should().Be(1);
        var recipient = _capturedMessage.To.First() as MailboxAddress;
        recipient!.Address.Should().Be("user@example.com");
        recipient.Name.Should().Be("Test User");
    }

    // A test email must not be mistakable for a real price alert in an inbox. The Discord test
    // reuses the price-alert payload safely because a channel message is obviously a test; an
    // email subject reading "Price Alert: Test Product dropped to USD 29.99!" is not.
    [Fact]
    public async Task SendTestEmailAsync_DoesNotLookLikeAPriceAlert()
    {
        await _service.SendTestEmailAsync("user@example.com", "Test User", TestContext.Current.CancellationToken);

        _capturedMessage.Should().NotBeNull();
        _capturedMessage!.Subject.Should().NotContain("Price Alert");
        _capturedMessage.Subject.Should().Contain("Test");
    }

    [Fact]
    public async Task SendTestEmailAsync_SendsFromTheConfiguredAddress()
    {
        await _service.SendTestEmailAsync("user@example.com", "Test User", TestContext.Current.CancellationToken);

        var sender = _capturedMessage!.From.First() as MailboxAddress;
        sender!.Address.Should().Be("noreply@ophi.app");
        sender.Name.Should().Be("Ophi");
    }

    // The body is the self-hoster's confirmation that SMTP works end to end, so it must say so
    // in words — and it must never echo the SMTP password back out over the wire.
    [Fact]
    public async Task SendTestEmailAsync_ExplainsWhatTheEmailIsAndLeaksNoCredentials()
    {
        await _service.SendTestEmailAsync("user@example.com", "Test User", TestContext.Current.CancellationToken);

        _capturedMessage!.HtmlBody.Should().Contain("SMTP");
        _capturedMessage.HtmlBody.Should().NotContain("testpass");
        _capturedMessage.HtmlBody.Should().NotContain("testuser");
    }

    [Fact]
    public async Task SendTestEmailAsync_ConnectsToSmtpServer()
    {
        await _service.SendTestEmailAsync("user@example.com", "Test User", TestContext.Current.CancellationToken);

        _smtpClientMock.Verify(
            x => x.ConnectAsync("smtp.test.com", 587, SecureSocketOptions.StartTls, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendTestEmailAsync_WithSmtpError_ThrowsException()
    {
        _smtpClientMock
            .Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SecureSocketOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection refused"));

        await _service.Invoking(s => s.SendTestEmailAsync("user@example.com", "Test User"))
            .Should().ThrowAsync<Exception>()
            .WithMessage("Connection refused");
    }

    #endregion

    #region IsConfigured Tests

    // DependencyInjection throws when SMTP_HOST is set but any of USER/PASS/FROM is missing, so
    // "host present" already implies "all four present". Do not expand this into a four-field check.
    [Fact]
    public void IsConfigured_WithSmtpHost_IsTrue()
    {
        new EmailSettings { SmtpHost = "smtp.test.com" }.IsConfigured.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IsConfigured_WithoutSmtpHost_IsFalse(string host)
    {
        new EmailSettings { SmtpHost = host }.IsConfigured.Should().BeFalse();
    }

    #endregion

    #region SendPasswordResetAsync Tests

    [Fact]
    public async Task SendPasswordResetAsync_WithValidData_SendsEmailWithCorrectSubject()
    {
        // Arrange
        var email = "user@example.com";
        var resetToken = "abc123token";

        // Act
        await _service.SendPasswordResetAsync(email, resetToken, TestContext.Current.CancellationToken);

        // Assert
        _capturedMessage.Should().NotBeNull();
        _capturedMessage!.Subject.Should().Be("Reset Your Ophi Password");
    }

    [Fact]
    public async Task SendPasswordResetAsync_SendsEmailToCorrectRecipient()
    {
        // Arrange
        var email = "resetuser@example.com";
        var resetToken = "token123";

        // Act
        await _service.SendPasswordResetAsync(email, resetToken, TestContext.Current.CancellationToken);

        // Assert
        _capturedMessage.Should().NotBeNull();
        var recipient = _capturedMessage!.To.First() as MailboxAddress;
        recipient!.Address.Should().Be("resetuser@example.com");
    }

    [Fact]
    public async Task SendPasswordResetAsync_IncludesResetTokenInBody()
    {
        // Arrange
        var email = "user@example.com";
        var resetToken = "unique-reset-token-12345";

        // Act
        await _service.SendPasswordResetAsync(email, resetToken, TestContext.Current.CancellationToken);

        // Assert
        _capturedMessage.Should().NotBeNull();
        _capturedMessage!.HtmlBody.Should().Contain("unique-reset-token-12345");
    }

    [Fact]
    public async Task SendPasswordResetAsync_UsesAppUrlFromSettings()
    {
        // Arrange
        const string email = "user@example.com";
        const string resetToken = "token123";

        // Act
        await _service.SendPasswordResetAsync(email, resetToken, TestContext.Current.CancellationToken);

        // Assert — URL must match the actual SvelteKit route at `/auth/reset-password`,
        // not a bare `/reset-password` (the email previously linked to a 404 because the
        // frontend never had a top-level reset-password route).
        _capturedMessage.Should().NotBeNull();
        _capturedMessage!.HtmlBody.Should().Contain("https://ophi.example.com/auth/reset-password?token=token123");
    }

    [Fact]
    public async Task SendPasswordResetAsync_ConnectsToSmtpServer()
    {
        // Arrange
        const string email = "user@example.com";
        const string resetToken = "token123";

        // Act
        await _service.SendPasswordResetAsync(email, resetToken, TestContext.Current.CancellationToken);

        // Assert
        _smtpClientMock.Verify(
            x => x.ConnectAsync("smtp.test.com", 587, SecureSocketOptions.StartTls, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendPasswordResetAsync_WithSmtpError_ThrowsException()
    {
        // Arrange
        const string email = "user@example.com";
        const string resetToken = "token123";

        _smtpClientMock
            .Setup(x => x.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()))
            .ThrowsAsync(new Exception("Send failed"));

        // Act & Assert
        await _service.Invoking(s => s.SendPasswordResetAsync(email, resetToken))
            .Should().ThrowAsync<Exception>()
            .WithMessage("Send failed");
    }

    #endregion
}
