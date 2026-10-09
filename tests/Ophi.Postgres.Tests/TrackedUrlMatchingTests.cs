using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Products;
using Ophi.TestHelpers;
using Wolverine;

namespace Ophi.Postgres.Tests;

/// <summary>
/// The duplicate check and the URL lookup (#49) on real Postgres: the user-scoped load of tracked URLs
/// must translate, and a URL that differs only by tracking noise must collide with — and be found
/// as — the product that holds it, scoped to its owner.
/// </summary>
[Collection("Postgres")]
public class TrackedUrlMatchingTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AddProduct_ThenLookupAndDuplicate_MatchByKey_OnPostgres()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var url = $"https://shop.example/p/{Guid.NewGuid():N}";
        var noisy = url.Replace("https://", "https://www.") + "?utm_source=mail&fbclid=x#reviews";

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            ctx.Users.Add(TestEntityFactory.User(otherUserId).Build());
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        AddProduct.Response created;
        await using (var ctx = fixture.CreateContext())
        {
            created = await new AddProduct.Handler(ctx, Mock.Of<IMessageBus>(), NullLogger<AddProduct.Handler>.Instance)
                .Handle(new AddProduct.Command(url) { UserId = userId }, TestContext.Current.CancellationToken);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var found = await new LookupProduct.Handler(ctx)
                .Handle(new LookupProduct.Query(userId, noisy), TestContext.Current.CancellationToken);
            found.ProductId.Should().Be(created.Id);

            var otherUsersLookup = () => new LookupProduct.Handler(ctx)
                .Handle(new LookupProduct.Query(otherUserId, noisy), TestContext.Current.CancellationToken);
            await otherUsersLookup.Should().ThrowAsync<NotFoundException>();
        }

        await using (var ctx = fixture.CreateContext())
        {
            var duplicate = () => new AddProduct.Handler(ctx, Mock.Of<IMessageBus>(), NullLogger<AddProduct.Handler>.Instance)
                .Handle(new AddProduct.Command(noisy) { UserId = userId }, TestContext.Current.CancellationToken);
            (await duplicate.Should().ThrowAsync<ConflictException>()).Which.ProductId.Should().Be(created.Id);
        }
    }
}
