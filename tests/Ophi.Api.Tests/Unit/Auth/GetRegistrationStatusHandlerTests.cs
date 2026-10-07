using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Ophi.Api.Features.Auth;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Auth;

public class GetRegistrationStatusHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;

    public GetRegistrationStatusHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
    }

    private GetRegistrationStatus.Handler CreateHandler(bool registrationEnabled) =>
        new(_dbContext, Options.Create(new RegistrationSettings { Enabled = registrationEnabled }));

    private async Task SeedUserAsync()
    {
        _dbContext.Users.Add(TestEntityFactory.User().Build());
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_WhenRegistrationEnabled_ReturnsOpen()
    {
        await SeedUserAsync();

        var result = await CreateHandler(registrationEnabled: true)
            .Handle(new GetRegistrationStatus.Query(), TestContext.Current.CancellationToken);

        result.Open.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenRegistrationDisabledAndAUserExists_ReturnsClosed()
    {
        await SeedUserAsync();

        var result = await CreateHandler(registrationEnabled: false)
            .Handle(new GetRegistrationStatus.Query(), TestContext.Current.CancellationToken);

        result.Open.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenRegistrationDisabledAndNoUsersExist_ReturnsOpen()
    {
        var result = await CreateHandler(registrationEnabled: false)
            .Handle(new GetRegistrationStatus.Query(), TestContext.Current.CancellationToken);

        result.Open.Should().BeTrue();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
