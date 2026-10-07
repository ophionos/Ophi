namespace Ophi.Api.Tests.Integration;

/// <summary>
/// Base class for integration tests that need a clean database per test.
/// Reuses the shared <see cref="OphiWebApplicationFactory"/> (paid once via
/// <c>IClassFixture</c>) but resets the SQLite schema before each test runs.
///
/// Use this when a test class asserts on "zero X" or otherwise can't tolerate
/// sibling-test leftovers. Tests that already isolate via <c>Guid.NewGuid()</c>
/// emails / per-user data don't need this — the shared-factory pattern is fine.
/// </summary>
public abstract class IsolatedIntegrationTest : IAsyncLifetime
{
    protected readonly OphiWebApplicationFactory Factory;

    protected IsolatedIntegrationTest(OphiWebApplicationFactory factory)
    {
        Factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        await Factory.ResetDatabaseAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
