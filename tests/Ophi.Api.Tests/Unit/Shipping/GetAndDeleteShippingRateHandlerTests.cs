using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Shipping;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Shipping;

public class GetAndDeleteShippingRateHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _db;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();

    public GetAndDeleteShippingRateHandlerTests()
    {
        (_db, _connection) = TestDbContextFactory.Create();
        _db.Users.Add(TestEntityFactory.User(_userId).Build());
        _db.Users.Add(TestEntityFactory.User(_otherUserId).Build());
        _db.SaveChanges();
    }

    private void Seed(Guid userId, string host, decimal amount = 5m, string currency = "EUR", decimal? freeAbove = null)
    {
        _db.ShippingRates.Add(new ShippingRate
        {
            Id = Guid.NewGuid(), UserId = userId, Host = host, Amount = amount, Currency = currency, FreeAbove = freeAbove
        });
        _db.SaveChanges();
    }

    private GetShippingRates.Handler GetHandler() => new(_db, NullLogger<GetShippingRates.Handler>.Instance);
    private DeleteShippingRate.Handler DeleteHandler() => new(_db, NullLogger<DeleteShippingRate.Handler>.Instance);

    [Fact]
    public async Task GetHandle_ReturnsOwnRatesOrderedByHost()
    {
        Seed(_userId, "zalando.de", 3m, "EUR", 50m);
        Seed(_userId, "amazon.es");
        Seed(_otherUserId, "aaa.test");

        var result = await GetHandler().Handle(new GetShippingRates.Query(_userId), TestContext.Current.CancellationToken);

        result.Items.Should().Equal(
            new GetShippingRates.Item("amazon.es", 5m, "EUR", null),
            new GetShippingRates.Item("zalando.de", 3m, "EUR", 50m));
    }

    [Fact]
    public async Task GetHandle_WithNoRates_ReturnsEmpty()
    {
        var result = await GetHandler().Handle(new GetShippingRates.Query(_userId), TestContext.Current.CancellationToken);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHandle_DoesNotShowAnotherUsersRates()
    {
        Seed(_userId, "amazon.es");

        var result = await GetHandler().Handle(new GetShippingRates.Query(_otherUserId), TestContext.Current.CancellationToken);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteHandle_RemovesTheRate()
    {
        Seed(_userId, "amazon.es");

        await DeleteHandler().Handle(new DeleteShippingRate.Command("amazon.es", _userId), TestContext.Current.CancellationToken);

        (await _db.ShippingRates.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteHandle_NormalizesTheHost()
    {
        Seed(_userId, "amazon.es");

        await DeleteHandler().Handle(new DeleteShippingRate.Command("WWW.Amazon.ES", _userId), TestContext.Current.CancellationToken);

        (await _db.ShippingRates.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteHandle_WithMissingRate_ThrowsNotFound()
    {
        var act = () => DeleteHandler().Handle(new DeleteShippingRate.Command("amazon.es", _userId), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeleteHandle_ForAnotherUsersRate_ThrowsNotFoundAndKeepsTheRow()
    {
        Seed(_userId, "amazon.es");

        var act = () => DeleteHandler().Handle(new DeleteShippingRate.Command("amazon.es", _otherUserId), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
        (await _db.ShippingRates.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
