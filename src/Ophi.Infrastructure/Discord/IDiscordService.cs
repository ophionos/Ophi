using Ophi.Domain.Enums;

namespace Ophi.Infrastructure.Discord;

public interface IDiscordService
{
    /// <summary>Posts to the user's own webhook; a blank URL is a no-op.</summary>
    Task SendPriceAlertAsync(DiscordPriceAlert alert, string webhookUrl, CancellationToken cancellationToken = default);
}

/// <param name="Condition">
/// Determines how <paramref name="TargetPrice"/> is rendered — see <c>AlertTargetFormatter</c>.
/// Null falls back to the amount rendering.
/// </param>
public record DiscordPriceAlert(
    string ProductName,
    string ProductUrl,
    decimal CurrentPrice,
    decimal TargetPrice,
    string Currency,
    AlertCondition? Condition = null
);
