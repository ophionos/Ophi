using System.Net;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Ophi.Infrastructure.Formatting;

namespace Ophi.Infrastructure.Email;

public class EmailService(IOptions<EmailSettings> settings, ILogger<EmailService> logger, ISmtpClientFactory smtpClientFactory) : IEmailService
{
    private readonly EmailSettings _settings = settings.Value;

    public async Task SendPriceAlertAsync(PriceAlertEmail alert, CancellationToken cancellationToken = default)
    {
        var subject = $"Price Alert: {alert.ProductName} dropped to {alert.Currency} {alert.CurrentPrice:F2}!";
        // The product name (and so the whole body) is scraped from the merchant's page: encode
        // every interpolated value, or the page controls markup in the user's inbox.
        var body = $"""
            <h2>Price Alert!</h2>
            <p>Great news! The product you're tracking has reached your target price.</p>

            <h3>{Html(alert.ProductName)}</h3>
            <p><strong>Current Price:</strong> {Html(alert.Currency)} {alert.CurrentPrice:F2}</p>
            <p><strong>Your Target:</strong> {Html(AlertTargetFormatter.Describe(alert.TargetPrice, alert.Condition, alert.Currency))}</p>

            <p><a href="{Html(alert.ProductUrl)}">View Product</a></p>

            <hr />
            <p style="color: #666; font-size: 12px;">
                This alert was sent by Ophi Price Tracker.
            </p>
            """;

        await SendEmailAsync(alert.ToEmail, alert.ToName, subject, body, cancellationToken);
    }

    public async Task SendPasswordResetAsync(string email, string resetToken, CancellationToken cancellationToken = default)
    {
        var resetUrl = $"{_settings.AppUrl}/auth/reset-password?token={resetToken}";
        const string subject = "Reset Your Ophi Password";
        var body = $"""
            <h2>Password Reset Request</h2>
            <p>You requested to reset your password. Click the link below to continue:</p>

            <p><a href="{Html(resetUrl)}">Reset Password</a></p>

            <p>If you didn't request this, you can safely ignore this email.</p>
            <p>This link will expire in 1 hour.</p>

            <hr />
            <p style="color: #666; font-size: 12px;">
                Ophi Price Tracker
            </p>
            """;

        await SendEmailAsync(email, "", subject, body, cancellationToken);
    }

    public async Task SendTestEmailAsync(string toEmail, string toName, CancellationToken cancellationToken = default)
    {
        // Deliberately NOT the price-alert template. A test that arrives titled
        // "Price Alert: Test Product dropped to USD 29.99!" is indistinguishable from a real alert
        // in an inbox, and users act on it. (The Discord test can reuse the alert payload because a
        // channel message is obviously a test; email has no such context.)
        const string subject = "Ophi SMTP Test Email";
        var body = """
            <h2>Your email notifications are working</h2>
            <p>This is a test message from Ophi. If you are reading it, your server's SMTP
            settings are correct and price alerts will reach this address.</p>

            <hr />
            <p style="color: #666; font-size: 12px;">
                Ophi Price Tracker &mdash; you received this because someone signed in to this
                account requested a test email.
            </p>
            """;

        await SendEmailAsync(toEmail, toName, subject, body, cancellationToken);
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private async Task SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = smtpClientFactory.Create();
            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(_settings.SmtpUser, _settings.SmtpPass, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            throw;
        }
    }
}

public class EmailSettings
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPass { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Ophi";
    public string AppUrl { get; set; } = string.Empty;

    /// <summary>
    /// Whether this server can send email at all. The single owner of that question — read it
    /// rather than re-deriving the check.
    /// </summary>
    /// <remarks>
    /// Testing only <see cref="SmtpHost"/> is deliberate and sufficient: <c>AddInfrastructure</c>
    /// throws at startup when SMTP_HOST is set but any of SMTP_USER / SMTP_PASS / SMTP_FROM is
    /// missing, so a present host already implies all four are present. Do not expand this into a
    /// four-field check.
    /// </remarks>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SmtpHost);
}
