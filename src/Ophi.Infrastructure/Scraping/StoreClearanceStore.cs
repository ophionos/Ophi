using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Infrastructure.Scraping;

/// <summary>Saved anti-bot clearances (<see cref="StoreClearance"/>), per user and host.</summary>
public interface IStoreClearanceStore
{
    /// <summary>The unexpired clearance for this user and host, or null. Not tracked.</summary>
    Task<StoreClearance?> FindAsync(Guid userId, string host, CancellationToken cancellationToken);

    /// <summary>Saves a clearance, replacing an earlier one for the same user and host.</summary>
    Task SaveAsync(Guid userId, string host, string storageState, string userAgent, CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, string host, CancellationToken cancellationToken);
}

/// <summary>
/// Reads and deletes run as single statements, without <c>SaveChanges</c>: the worker calls them inside
/// a scrape whose handler owns the same scoped <see cref="OphiDbContext"/>, and must not flush its
/// pending changes early.
/// </summary>
public sealed class StoreClearanceStore(OphiDbContext dbContext, TimeProvider timeProvider) : IStoreClearanceStore
{
    public async Task<StoreClearance?> FindAsync(Guid userId, string host, CancellationToken cancellationToken)
    {
        var key = host.ToLowerInvariant();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return await dbContext.StoreClearances
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Host == key && c.ExpiresAt > now, cancellationToken);
    }

    public async Task SaveAsync(Guid userId, string host, string storageState, string userAgent, CancellationToken cancellationToken)
    {
        await DeleteAsync(userId, host, cancellationToken);
        dbContext.StoreClearances.Add(
            StoreClearance.Create(userId, host, storageState, userAgent, timeProvider.GetUtcNow().UtcDateTime));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, string host, CancellationToken cancellationToken)
    {
        var key = host.ToLowerInvariant();
        await dbContext.StoreClearances
            .Where(c => c.UserId == userId && c.Host == key)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
