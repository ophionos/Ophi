using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.TestHelpers;

namespace Ophi.Postgres.Tests;

/// <summary>
/// UX-5 account deletion is a hard delete that relies on ON DELETE CASCADE for every
/// user-rooted FK. SQLite recreates the schema from the EF model, so its cascades can
/// false-green against production; this verifies the real Postgres schema (as produced by
/// the migration history) cascades the full ownership graph — and only that user's graph.
/// </summary>
[Collection("Postgres")]
public class AccountDeletionCascadeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task DeletingUser_CascadesEntireOwnershipGraph_AndSparesOtherUsers()
    {
        var doomedId = Guid.NewGuid();
        var survivorId = Guid.NewGuid();

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(doomedId).Build());
            ctx.Users.Add(TestEntityFactory.User(survivorId).Build());
            SeedOwnershipGraph(ctx, doomedId);
            SeedOwnershipGraph(ctx, survivorId);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Mirror the DeleteAccount handler: load just the user, remove, save. Everything
        // else must go via the database cascade, not EF client-side cascade.
        await using (var ctx = fixture.CreateContext())
        {
            var doomed = await ctx.Users.SingleAsync(u => u.Id == doomedId, TestContext.Current.CancellationToken);
            ctx.Users.Remove(doomed);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.Users.AnyAsync(u => u.Id == doomedId, TestContext.Current.CancellationToken)).Should().BeFalse();
            (await ctx.Products.CountAsync(p => p.UserId == doomedId, TestContext.Current.CancellationToken)).Should().Be(0);
            (await ctx.ProductUrls.CountAsync(u => u.Product.UserId == doomedId, TestContext.Current.CancellationToken)).Should().Be(0);
            (await ctx.PricePoints.CountAsync(p => p.Product.UserId == doomedId, TestContext.Current.CancellationToken)).Should().Be(0);
            (await ctx.Alerts.CountAsync(a => a.UserId == doomedId, TestContext.Current.CancellationToken)).Should().Be(0);
            (await ctx.Notifications.CountAsync(n => n.UserId == doomedId, TestContext.Current.CancellationToken)).Should().Be(0);
            (await ctx.Tags.CountAsync(t => t.UserId == doomedId, TestContext.Current.CancellationToken)).Should().Be(0);
            (await ctx.ComparisonGroups.CountAsync(g => g.UserId == doomedId, TestContext.Current.CancellationToken)).Should().Be(0);
            (await ctx.StoreConfigurations.CountAsync(s => s.UserId == doomedId, TestContext.Current.CancellationToken)).Should().Be(0);
            (await ctx.WebhookTargets.CountAsync(w => w.UserId == doomedId, TestContext.Current.CancellationToken)).Should().Be(0);
            (await ctx.ApiKeys.CountAsync(k => k.UserId == doomedId, TestContext.Current.CancellationToken)).Should().Be(0);

            // The survivor's identical graph is untouched.
            (await ctx.Users.AnyAsync(u => u.Id == survivorId, TestContext.Current.CancellationToken)).Should().BeTrue();
            (await ctx.Products.CountAsync(p => p.UserId == survivorId, TestContext.Current.CancellationToken)).Should().Be(1);
            (await ctx.ProductUrls.CountAsync(u => u.Product.UserId == survivorId, TestContext.Current.CancellationToken)).Should().Be(1);
            (await ctx.PricePoints.CountAsync(p => p.Product.UserId == survivorId, TestContext.Current.CancellationToken)).Should().Be(1);
            (await ctx.Alerts.CountAsync(a => a.UserId == survivorId, TestContext.Current.CancellationToken)).Should().Be(1);
            (await ctx.Notifications.CountAsync(n => n.UserId == survivorId, TestContext.Current.CancellationToken)).Should().Be(1);
            (await ctx.Tags.CountAsync(t => t.UserId == survivorId, TestContext.Current.CancellationToken)).Should().Be(1);
            (await ctx.ComparisonGroups.CountAsync(g => g.UserId == survivorId, TestContext.Current.CancellationToken)).Should().Be(1);
            (await ctx.StoreConfigurations.CountAsync(s => s.UserId == survivorId, TestContext.Current.CancellationToken)).Should().Be(1);
            (await ctx.WebhookTargets.CountAsync(w => w.UserId == survivorId, TestContext.Current.CancellationToken)).Should().Be(1);
            (await ctx.ApiKeys.CountAsync(k => k.UserId == survivorId, TestContext.Current.CancellationToken)).Should().Be(1);
        }
    }

    /// <summary>One row in every user-owned table, wired together where the model allows.</summary>
    private static void SeedOwnershipGraph(Ophi.Infrastructure.Persistence.OphiDbContext ctx, Guid userId)
    {
        var product = TestEntityFactory.Product(userId).Named($"Product {userId:N}").Build();
        var productUrl = TestEntityFactory.ProductUrl(product.Id).Build();
        var tag = new Tag { Id = Guid.NewGuid(), UserId = userId, Name = $"tag-{userId:N}" };

        ctx.Products.Add(product);
        ctx.ProductUrls.Add(productUrl);
        ctx.PricePoints.Add(new PricePoint
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ProductUrlId = productUrl.Id,
            Price = 99.99m,
            Currency = "USD",
            RecordedAt = DateTime.UtcNow
        });
        ctx.Alerts.Add(TestEntityFactory.Alert(product.Id, userId).Build());
        ctx.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductId = product.Id,
            Title = "Price alert",
            Message = "Test",
            Type = NotificationType.PriceAlert
        });
        ctx.Tags.Add(tag);
        ctx.Set<ProductTag>().Add(new ProductTag { ProductId = product.Id, TagId = tag.Id });
        ctx.ComparisonGroups.Add(new ComparisonGroup
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = $"group-{userId:N}",
            Products = [product]
        });
        ctx.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StoreId = $"store-{userId:N}",
            Name = "Custom Store"
        });
        ctx.WebhookTargets.Add(new WebhookTarget
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Webhook",
            Url = "https://example.com/hook",
            Events = ["price_changed"]
        });
        ctx.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Key",
            KeyHash = Convert.ToHexString(Guid.NewGuid().ToByteArray())
        });
    }
}
