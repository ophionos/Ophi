using Ophi.Domain.Entities;

namespace Ophi.Worker.Handlers;

internal static class PageFetchDelayHelper
{
    public static Task ApplyAsync(User user, CancellationToken cancellationToken)
    {
        var delaySeconds = user.PageFetchDelaySeconds ?? 0;
        return delaySeconds > 0
            ? Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken)
            : Task.CompletedTask;
    }
}
