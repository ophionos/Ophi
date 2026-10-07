using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Fx;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Worker.Services;

/// <summary>
/// Keeps the <c>ExchangeRates</c> table fresh for display-currency conversion. Called from the
/// dispatcher loop like <see cref="DataRetentionService"/>. It fetches only while some user has a
/// display currency set, so a server nobody uses this on makes no outbound call, and at most every
/// <see cref="Cooldown"/> (the ECB publishes once per working day).
/// </summary>
public class ExchangeRateRefresher(
    IServiceProvider serviceProvider,
    EcbRatesClient client,
    TimeProvider timeProvider,
    ILogger<ExchangeRateRefresher> logger)
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(6);

    private DateTime _lastAttemptUtc = DateTime.MinValue;
    private int _running;

    public async Task RefreshIfDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (now - _lastAttemptUtc < Cooldown) return;
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;

        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();

            // Not stamping the attempt here: the first user to opt in gets rates on the next tick.
            if (!await db.Users.AnyAsync(u => u.DisplayCurrency != null, cancellationToken))
                return;

            // Stamp before fetching so a failing feed is retried after the cooldown, not every tick.
            _lastAttemptUtc = now;

            var snapshot = await client.FetchAsync(cancellationToken);

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.ExchangeRates.ExecuteDeleteAsync(cancellationToken);
            db.ExchangeRates.Add(new ExchangeRate { Currency = "EUR", UnitsPerEur = 1m, AsOf = snapshot.AsOf, FetchedAt = now });
            foreach (var (currency, units) in snapshot.UnitsPerEur)
            {
                if (currency == "EUR") continue;
                db.ExchangeRates.Add(new ExchangeRate { Currency = currency, UnitsPerEur = units, AsOf = snapshot.AsOf, FetchedAt = now });
            }
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            logger.LogInformation("Exchange rates refreshed: {Count} currencies as of {AsOf:yyyy-MM-dd}",
                snapshot.UnitsPerEur.Count + 1, snapshot.AsOf);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the previous rates; the UI labels them with their date once they go stale.
            logger.LogWarning(ex, "Exchange rate refresh failed; keeping previous rates");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
