using System.Net;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Moq.Protected;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Fx;
using Ophi.Infrastructure.Persistence;
using Ophi.Worker.Services;

namespace Ophi.Infrastructure.Tests.Fx;

public class FxTests : IDisposable
{
    // Same shape as the live feed (checked 2026-09-25): default namespace, single-quoted attributes.
    private const string EcbXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
        	<gesmes:subject>Reference rates</gesmes:subject>
        	<Cube>
        		<Cube time='2026-09-24'>
        			<Cube currency='USD' rate='1.1367'/>
        			<Cube currency='JPY' rate='180.57'/>
        			<Cube currency='GBP' rate='0.85986'/>
        		</Cube>
        	</Cube>
        </gesmes:Envelope>
        """;

    private readonly Mock<HttpMessageHandler> _http = new();
    private int _calls;
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero));

    public FxTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<OphiDbContext>(o => o.UseSqlite(_connection));
        _services = services.BuildServiceProvider();
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<OphiDbContext>().Database.EnsureCreated();
    }

    private void Respond(HttpStatusCode status, string body = EcbXml) =>
        _http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback(() => _calls++)
            .ReturnsAsync(() => new HttpResponseMessage(status) { Content = new StringContent(body) });

    private EcbRatesClient Client() => new(new HttpClient(_http.Object), NullLogger<EcbRatesClient>.Instance);

    private ExchangeRateRefresher Refresher() =>
        new(_services, Client(), _time, NullLogger<ExchangeRateRefresher>.Instance);

    private OphiDbContext Db() => _services.CreateScope().ServiceProvider.GetRequiredService<OphiDbContext>();

    private async Task AddUserAsync(string? displayCurrency)
    {
        await using var db = Db();
        db.Users.Add(new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid()}@x.test", Name = "U", PasswordHash = "h", DisplayCurrency = displayCurrency });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Client_ParsesDateAndRates_FromTheEcbFormat()
    {
        Respond(HttpStatusCode.OK);

        var snapshot = await Client().FetchAsync(TestContext.Current.CancellationToken);

        snapshot.AsOf.Should().Be(new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc));
        snapshot.UnitsPerEur.Should().Contain(new KeyValuePair<string, decimal>("USD", 1.1367m))
            .And.Contain(new KeyValuePair<string, decimal>("GBP", 0.85986m))
            .And.HaveCount(3);
    }

    [Fact]
    public async Task Client_OnMalformedFeed_Throws()
    {
        Respond(HttpStatusCode.OK, "<html>maintenance</html>");

        var act = () => Client().FetchAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<FormatException>();
    }

    [Fact]
    public async Task Refresher_WithNoUserWantingConversion_MakesNoOutboundCall()
    {
        Respond(HttpStatusCode.OK);
        await AddUserAsync(null);

        await Refresher().RefreshIfDueAsync(TestContext.Current.CancellationToken);

        _calls.Should().Be(0);
    }

    [Fact]
    public async Task Refresher_StoresRatesPlusEur_AndRespectsItsCooldown()
    {
        Respond(HttpStatusCode.OK);
        await AddUserAsync("GBP");
        var refresher = Refresher();

        await refresher.RefreshIfDueAsync(TestContext.Current.CancellationToken);
        await refresher.RefreshIfDueAsync(TestContext.Current.CancellationToken);

        _calls.Should().Be(1, "a second call inside the cooldown must not refetch");
        await using var db = Db();
        var rates = await db.ExchangeRates.ToListAsync(TestContext.Current.CancellationToken);
        rates.Select(r => r.Currency).Should().BeEquivalentTo(["EUR", "USD", "JPY", "GBP"]);
        rates.Single(r => r.Currency == "EUR").UnitsPerEur.Should().Be(1m);

        _time.Advance(TimeSpan.FromHours(7));
        await refresher.RefreshIfDueAsync(TestContext.Current.CancellationToken);
        _calls.Should().Be(2);
    }

    [Fact]
    public async Task Refresher_OnFetchFailure_KeepsThePreviousRates()
    {
        Respond(HttpStatusCode.OK);
        await AddUserAsync("GBP");
        var refresher = Refresher();
        await refresher.RefreshIfDueAsync(TestContext.Current.CancellationToken);

        Respond(HttpStatusCode.ServiceUnavailable);
        _time.Advance(TimeSpan.FromHours(7));
        await refresher.RefreshIfDueAsync(TestContext.Current.CancellationToken);

        await using var db = Db();
        (await db.ExchangeRates.CountAsync(TestContext.Current.CancellationToken)).Should().Be(4);
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
