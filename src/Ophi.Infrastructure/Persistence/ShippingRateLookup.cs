using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;

namespace Ophi.Infrastructure.Persistence;

/// <summary>
/// Loads a user's shipping rates once, keyed by normalized host, for <see cref="Domain.Services.LandedPrice"/>.
/// </summary>
public static class ShippingRateLookup
{
    public static async Task<Func<string, ShippingRate?>> LoadAsync(
        OphiDbContext dbContext, Guid userId, CancellationToken cancellationToken)
    {
        var rates = await dbContext.ShippingRates
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .ToDictionaryAsync(r => r.Host, cancellationToken);

        return host => rates.GetValueOrDefault(host);
    }
}
