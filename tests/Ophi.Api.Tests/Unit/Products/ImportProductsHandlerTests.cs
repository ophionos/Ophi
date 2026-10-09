using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Ophi.Api.Features.Products;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Ophi.TestHelpers;
using Wolverine;

namespace Ophi.Api.Tests.Unit.Products;

public class ImportProductsHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly ImportProducts.Handler _handler;
    private readonly Guid _userId = Guid.NewGuid();

    public ImportProductsHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new ImportProducts.Handler(_dbContext, Mock.Of<IMessageBus>(),
            Options.Create(new AlertSettings { MaxAlertsPerUser = 1 }), NullLogger<ImportProducts.Handler>.Instance);

        _dbContext.Users.Add(new User { Id = _userId, Email = "import@example.com", Name = "Importer", PasswordHash = "hash" });
        _dbContext.SaveChanges();
    }

    private Task<ImportProducts.ImportResponse> Import(params ImportProducts.ImportRow[] rows) =>
        _handler.Handle(new ImportProducts.Command(rows) { UserId = _userId }, TestContext.Current.CancellationToken);

    private static ImportProducts.ImportRow Row(int line, string url, string? name = null, decimal? target = null, string? tags = null) =>
        new(line, url, name, target, tags);

    // The import saves every row in one SaveChanges. A value longer than its varchar column passes
    // SQLite and fails Postgres with a 500, which would lose every valid row in the file with it.
    // Each oversized row must become a per-line error instead.
    [Fact]
    public async Task Handle_WithNameLongerThanColumn_ReportsLineErrorAndImportsOtherRows()
    {
        var result = await Import(
            Row(2, "https://shop.example.com/long", name: new string('n', 501)),
            Row(3, "https://shop.example.com/ok", name: "Fine"));

        result.Added.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().StartWith("Line 2:");
        (await _dbContext.Products.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithRowsDifferingOnlyByTrackingParams_ImportsOneAndSkipsTheRest()
    {
        var result = await Import(
            Row(2, "https://shop.example.com/item"),
            Row(3, "https://shop.example.com/item?utm_source=sheet"),
            Row(4, "https://www.shop.example.com/item#specs"));

        result.Added.Should().Be(1);
        result.Skipped.Should().Be(2);
    }

    [Fact]
    public async Task Handle_WithRowMatchingATrackedUrlByKey_SkipsIt()
    {
        await Import(Row(2, "https://shop.example.com/item"));

        var result = await Import(Row(2, "https://shop.example.com/item?fbclid=abc"));

        result.Added.Should().Be(0);
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithUrlLongerThanColumn_ReportsLineError()
    {
        var result = await Import(Row(2, "https://shop.example.com/" + new string('u', 2048)));

        result.Added.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Should().StartWith("Line 2:");
    }

    [Fact]
    public async Task Handle_WithTagLongerThanColumn_ReportsLineError()
    {
        var result = await Import(Row(2, "https://shop.example.com/tagged", tags: new string('t', 51)));

        result.Added.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Should().StartWith("Line 2:");
        (await _dbContext.Tags.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Handle_WithNonPositiveTargetPrice_ReportsLineError(decimal target)
    {
        // A price is > 0 (docs/agent-notes.md § Pricing); a target of 0 never fires and a negative
        // one is nonsense. Reject the row rather than create an alert that can't mean anything.
        var result = await Import(Row(2, "https://shop.example.com/zero", target: target));

        result.Added.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Should().StartWith("Line 2:");
        (await _dbContext.Alerts.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithRepeatedTagInRow_AddsTagOnce()
    {
        var result = await Import(Row(2, "https://shop.example.com/dup", tags: "sale, Sale,sale"));

        result.Added.Should().Be(1);
        (await _dbContext.ProductTags.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithMoreTargetsThanAlertCap_ImportsExtraAlertsPaused()
    {
        // Same rule as CreateAlert and ImportBackup: the per-user active-alert cap holds, and
        // alerts beyond it arrive paused rather than being dropped.
        var result = await Import(
            Row(2, "https://shop.example.com/one", target: 10m),
            Row(3, "https://shop.example.com/two", target: 20m));

        result.Added.Should().Be(2);
        var alerts = await _dbContext.Alerts.ToListAsync(TestContext.Current.CancellationToken);
        alerts.Should().HaveCount(2);
        alerts.Count(a => a.IsActive).Should().Be(1);
        result.Errors.Should().ContainSingle(e => e.Contains("paused"));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
