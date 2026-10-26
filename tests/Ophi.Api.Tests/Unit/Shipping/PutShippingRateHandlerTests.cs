using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Features.Shipping;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Shipping;

public class PutShippingRateHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _db;
    private readonly PutShippingRate.Handler _handler;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();

    public PutShippingRateHandlerTests()
    {
        (_db, _connection) = TestDbContextFactory.Create();
        _handler = new PutShippingRate.Handler(_db, NullLogger<PutShippingRate.Handler>.Instance);
        _db.Users.Add(TestEntityFactory.User(_userId).Build());
        _db.Users.Add(TestEntityFactory.User(_otherUserId).Build());
        _db.SaveChanges();
    }

    private static PutShippingRate.Command Cmd(string host, Guid userId, decimal amount = 4.99m, string currency = "EUR", decimal? freeAbove = null) =>
        new(host, amount, currency, freeAbove) { UserId = userId };

    [Fact]
    public async Task Handle_WithNewHost_CreatesTheRate()
    {
        var result = await _handler.Handle(Cmd("amazon.es", _userId, 4.99m, "EUR", 29m), TestContext.Current.CancellationToken);

        result.Should().Be(new PutShippingRate.Response("amazon.es", 4.99m, "EUR", 29m));
        var row = await _db.ShippingRates.SingleAsync(TestContext.Current.CancellationToken);
        row.UserId.Should().Be(_userId);
        row.Host.Should().Be("amazon.es");
        row.FreeAbove.Should().Be(29m);
    }

    [Fact]
    public async Task Handle_WithExistingHost_ReplacesTheRateInPlace()
    {
        await _handler.Handle(Cmd("amazon.es", _userId, 4.99m, "EUR", 29m), TestContext.Current.CancellationToken);

        var result = await _handler.Handle(Cmd("amazon.es", _userId, 0m, "GBP"), TestContext.Current.CancellationToken);

        result.Should().Be(new PutShippingRate.Response("amazon.es", 0m, "GBP", null));
        (await _db.ShippingRates.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Theory]
    [InlineData("WWW.Amazon.ES", "amazon.es")]
    [InlineData("  www.shop.test  ", "shop.test")]
    public async Task Handle_WithUnnormalizedHost_StoresTheNormalizedHost(string raw, string expected)
    {
        var result = await _handler.Handle(Cmd(raw, _userId), TestContext.Current.CancellationToken);

        result.Host.Should().Be(expected);
        (await _db.ShippingRates.SingleAsync(TestContext.Current.CancellationToken)).Host.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_WithLowercaseCurrency_StoresUppercase()
    {
        var result = await _handler.Handle(Cmd("amazon.es", _userId, currency: "eur"), TestContext.Current.CancellationToken);

        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task Handle_ByAnotherUser_CreatesTheirOwnRowAndLeavesTheFirstUntouched()
    {
        await _handler.Handle(Cmd("amazon.es", _userId, 4.99m), TestContext.Current.CancellationToken);

        await _handler.Handle(Cmd("amazon.es", _otherUserId, 9m), TestContext.Current.CancellationToken);

        var rows = await _db.ShippingRates.ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().HaveCount(2);
        rows.Single(r => r.UserId == _userId).Amount.Should().Be(4.99m);
        rows.Single(r => r.UserId == _otherUserId).Amount.Should().Be(9m);
    }

    private static TestValidationResult<PutShippingRate.Command> Validate(PutShippingRate.Command c) =>
        new PutShippingRate.Validator().TestValidate(c);

    [Fact]
    public void Validator_WithValidRate_Passes() =>
        Validate(Cmd("www.Amazon.es", _userId, 0m, "eur", 20m)).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Validator_WithNegativeAmount_Fails() =>
        Validate(Cmd("amazon.es", _userId, -0.01m)).ShouldHaveValidationErrorFor(x => x.Amount);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validator_WithNonPositiveFreeAbove_Fails(double value) =>
        Validate(Cmd("amazon.es", _userId, freeAbove: (decimal)value)).ShouldHaveValidationErrorFor(x => x.FreeAbove);

    [Theory]
    [InlineData("")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public void Validator_WithBadCurrency_Fails(string currency) =>
        Validate(Cmd("amazon.es", _userId, currency: currency)).ShouldHaveValidationErrorFor(x => x.Currency);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("amazon.es/path")]
    [InlineData("amazon.es:443")]
    [InlineData("ama zon.es")]
    [InlineData("www.")]
    public void Validator_WithBadHost_Fails(string host) =>
        Validate(Cmd(host, _userId)).ShouldHaveValidationErrorFor(x => x.Host);

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
