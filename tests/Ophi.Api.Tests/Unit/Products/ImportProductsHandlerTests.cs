using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ophi.Api.Features.Products;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
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
        _handler = new ImportProducts.Handler(_dbContext, Mock.Of<IMessageBus>(), NullLogger<ImportProducts.Handler>.Instance);

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

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
