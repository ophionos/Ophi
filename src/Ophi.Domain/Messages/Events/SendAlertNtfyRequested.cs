using System.Text;
using Ophi.Domain.Enums;
using Ophi.Domain.Logging;

namespace Ophi.Domain.Messages.Events;

/// <summary>ntfy counterpart of <see cref="SendAlertTelegramRequested"/>.</summary>
public record SendAlertNtfyRequested(
    Guid AlertId,
    [property: Sensitive] string TopicUrl,
    string ProductName,
    string ProductUrl,
    decimal CurrentPrice,
    decimal TargetPrice,
    string Currency,
    AlertCondition? Condition = null,
    string? Store = null)
{
    protected virtual bool PrintMembers(StringBuilder builder) => SensitivePrinter.PrintMembers(this, builder);
}
