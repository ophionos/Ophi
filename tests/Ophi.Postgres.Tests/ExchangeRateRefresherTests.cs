using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Ophi.Infrastructure.Fx;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;
using Ophi.Worker.Services;

namespace Ophi.Postgres.Tests;

/// <summary>
/// The refresher replaces the whole rate set inside one transaction (ExecuteDelete + inserts) and
/// stores numeric(18,6) rates — both provider-sensitive, so they are proven on real Postgres.
/// </summary>
[Collection("Postgres")]
public class ExchangeRateRefresherTests(PostgresFixture fixture)
{
    private sealed class FeedHandler(Func<string> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body()) });
    }

    private static string Feed(string date, string usd) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
        <Cube><Cube time='{date}'><Cube currency='USD' rate='{usd}'/><Cube currency='ISK' rate='138.00'/></Cube></Cube>
        </gesmes:Envelope>
        """;

    [Fact]
    public async Task Refresh_ReplacesTheRateSet_AndKeepsSixDecimalPlaces()
    {
        await using (var ctx = fixture.CreateContext())
        {
            // Rates are shared; start from a clean table so earlier runs don't leak into the assertions.
            await ctx.ExchangeRates.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            var user = TestEntityFactory.User().Build();
            user.DisplayCurrency = "USD";
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => fixture.CreateContext());
        await using var provider = services.BuildServiceProvider();

        var body = Feed("2026-09-24", "1.136712");
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero));
        var refresher = new ExchangeRateRefresher(
            provider,
            new EcbRatesClient(new HttpClient(new FeedHandler(() => body)), NullLogger<EcbRatesClient>.Instance),
            time,
            NullLogger<ExchangeRateRefresher>.Instance);

        await refresher.RefreshIfDueAsync(TestContext.Current.CancellationToken);

        body = Feed("2026-09-25", "1.140001");
        time.Advance(ExchangeRateRefresher.Cooldown + TimeSpan.FromMinutes(1));
        await refresher.RefreshIfDueAsync(TestContext.Current.CancellationToken);

        await using var verify = fixture.CreateContext();
        var rates = await verify.ExchangeRates.OrderBy(r => r.Currency).ToListAsync(TestContext.Current.CancellationToken);
        rates.Select(r => r.Currency).Should().Equal("EUR", "ISK", "USD");
        rates.Single(r => r.Currency == "USD").UnitsPerEur.Should().Be(1.140001m);
        rates.Should().OnlyContain(r => r.AsOf == new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc));
    }
}
