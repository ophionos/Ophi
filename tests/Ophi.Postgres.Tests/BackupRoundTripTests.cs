using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ophi.Api.Features.Account;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Settings;
using Ophi.TestHelpers;

namespace Ophi.Postgres.Tests;

/// <summary>
/// Export from one account, import into another, on real Postgres: exercises the import transaction,
/// the unique indexes it must respect (tag / group / store names per user) and timestamptz round-trips.
/// </summary>
[Collection("Postgres")]
public class BackupRoundTripTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ExportThenImport_IntoAnotherAccount_ReproducesTheData_AndIsIdempotent()
    {
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(source).Build());
            ctx.Users.Add(TestEntityFactory.User(target).Build());
            var tag = new Tag { Id = Guid.NewGuid(), UserId = source, Name = "Audio" };
            ctx.Tags.Add(tag);
            var product = TestEntityFactory.Product(source).Named("Sony XM5").Priced(299m).Build();
            var url = TestEntityFactory.ProductUrl(product.Id).WithUrl($"https://shop.test/{Guid.NewGuid()}").Priced(299m).Build();
            ctx.Products.Add(product);
            ctx.ProductUrls.Add(url);
            ctx.ProductTags.Add(new ProductTag { ProductId = product.Id, TagId = tag.Id });
            ctx.PricePoints.Add(new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url.Id, Price = 299m, Currency = "USD", RecordedAt = new DateTime(2026, 9, 20, 12, 30, 0, DateTimeKind.Utc) });
            ctx.Alerts.Add(TestEntityFactory.Alert(product.Id, source).WithTarget(250m).Build());
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        BackupBundle bundle;
        await using (var ctx = fixture.CreateContext())
        {
            bundle = await new ExportBackup.Handler(ctx, TimeProvider.System, NullLogger<ExportBackup.Handler>.Instance)
                .Handle(new ExportBackup.Query(source), TestContext.Current.CancellationToken);
        }

        async Task<ImportBackup.Response> ImportAsync()
        {
            await using var ctx = fixture.CreateContext();
            return await new ImportBackup.Handler(ctx, Options.Create(new AlertSettings()), NullLogger<ImportBackup.Handler>.Instance)
                .Handle(new ImportBackup.Command(bundle) { UserId = target }, TestContext.Current.CancellationToken);
        }

        var first = await ImportAsync();
        var second = await ImportAsync();

        first.ProductsAdded.Should().Be(1);
        first.TagsAdded.Should().Be(1);
        second.ProductsAdded.Should().Be(0, "re-importing the same file must not duplicate anything");
        second.ProductsSkipped.Should().Be(1);
        second.TagsAdded.Should().Be(0);

        await using var verify = fixture.CreateContext();
        var imported = await verify.Products.Include(p => p.ProductTags).Include(p => p.Alerts)
            .SingleAsync(p => p.UserId == target, TestContext.Current.CancellationToken);
        imported.ProductTags.Should().ContainSingle();
        imported.Alerts.Should().ContainSingle(a => a.TargetPrice == 250m && a.UserId == target);
        (await verify.PricePoints.SingleAsync(pp => pp.ProductId == imported.Id, TestContext.Current.CancellationToken))
            .RecordedAt.Should().Be(new DateTime(2026, 9, 20, 12, 30, 0, DateTimeKind.Utc));
    }
}
