using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Features.Fx;
using Ophi.Api.Features.Settings;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Settings;

/// <summary>Display-currency preference and the rates endpoint it reads (B-3).</summary>
public class DisplayCurrencyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Guid _userId = Guid.NewGuid();

    public DisplayCurrencyTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContext.Users.Add(TestEntityFactory.User(_userId).Build());
        _dbContext.SaveChanges();
    }

    private static UpdateSettings.Command Cmd(string? currency) => new(null, null, DisplayCurrency: currency);

    [Theory]
    [InlineData("USD")]
    [InlineData("eur")]
    [InlineData("")]
    public void Validator_AcceptsEcbCurrenciesAndEmpty(string value) =>
        new UpdateSettings.Validator().TestValidate(Cmd(value)).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("XYZ")]
    [InlineData("ARS")] // real ISO code, but the ECB publishes no rate for it — conversion would be a guess
    [InlineData("DOLLARS")]
    public void Validator_RejectsCurrenciesWithoutAnEcbRate(string value) =>
        new UpdateSettings.Validator().TestValidate(Cmd(value)).ShouldHaveValidationErrorFor(x => x.DisplayCurrency);

    [Fact]
    public async Task Update_NormalizesToUppercase_AndEmptyClears()
    {
        var handler = new UpdateSettings.Handler(_dbContext, NullLogger<UpdateSettings.Handler>.Instance);

        var set = await handler.Handle(Cmd("gbp") with { UserId = _userId }, TestContext.Current.CancellationToken);
        set.DisplayCurrency.Should().Be("GBP");

        var cleared = await handler.Handle(Cmd("") with { UserId = _userId }, TestContext.Current.CancellationToken);
        cleared.DisplayCurrency.Should().BeNull();
    }

    [Fact]
    public async Task GetFxRates_ReturnsRatesWithTheirDate()
    {
        var asOf = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
        _dbContext.ExchangeRates.AddRange(
            new ExchangeRate { Currency = "EUR", UnitsPerEur = 1m, AsOf = asOf, FetchedAt = asOf },
            new ExchangeRate { Currency = "USD", UnitsPerEur = 1.1367m, AsOf = asOf, FetchedAt = asOf });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new GetFxRates.Handler(_dbContext).Handle(new GetFxRates.Query(), TestContext.Current.CancellationToken);

        result.Base.Should().Be("EUR");
        result.AsOf.Should().Be(asOf);
        result.Rates.Should().Contain(new KeyValuePair<string, decimal>("USD", 1.1367m));
    }

    [Fact]
    public async Task GetFxRates_WithNoRatesYet_ReturnsEmpty()
    {
        var result = await new GetFxRates.Handler(_dbContext).Handle(new GetFxRates.Query(), TestContext.Current.CancellationToken);

        result.AsOf.Should().BeNull();
        result.Rates.Should().BeEmpty();
        result.Supported.Should().Contain(["EUR", "USD", "GBP"]).And.BeInAscendingOrder(StringComparer.Ordinal);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
