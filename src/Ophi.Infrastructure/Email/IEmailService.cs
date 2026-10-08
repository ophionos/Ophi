using Ophi.Domain.Enums;

namespace Ophi.Infrastructure.Email;

public interface IEmailService
{
    Task SendPriceAlertAsync(PriceAlertEmail alert, CancellationToken cancellationToken = default);
    Task SendPasswordResetAsync(string email, string resetToken, CancellationToken cancellationToken = default);

    /// <summary>Mails the link that confirms <paramref name="newEmail"/> as the login.</summary>
    Task SendEmailChangeConfirmationAsync(string newEmail, string token, CancellationToken cancellationToken = default);

    /// <summary>Tells the current address that a change to <paramref name="newEmail"/> was requested.</summary>
    Task SendEmailChangeNoticeAsync(string currentEmail, string newEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a "your SMTP works" confirmation. Callers must pass the authenticated user's own
    /// account email — never an address taken from a request body.
    /// </summary>
    Task SendTestEmailAsync(string toEmail, string toName, CancellationToken cancellationToken = default);
}

/// <param name="Condition">
/// Determines how <paramref name="TargetPrice"/> is rendered — see <c>AlertTargetFormatter</c>.
/// Null falls back to the amount rendering.
/// </param>
public record PriceAlertEmail(
    string ToEmail,
    string ToName,
    string ProductName,
    string ProductUrl,
    decimal CurrentPrice,
    decimal TargetPrice,
    string Currency,
    AlertCondition? Condition = null
);
