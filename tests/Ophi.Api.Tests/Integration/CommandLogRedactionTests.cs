using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Ophi.Api.Tests.Integration;

/// <summary>
/// Wolverine logs a failing message with its <c>ToString()</c> ("Invocation of {Message} failed!").
/// These tests drive real failures through the bus and assert the secret is not in any log line.
/// </summary>
public class CommandLogRedactionTests(LogCapturingFactory factory) : IClassFixture<LogCapturingFactory>, IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Login_WithWrongPassword_DoesNotLogTheAttemptedPassword()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"redact-{Guid.NewGuid()}@example.com";
        (await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { Email = email, Password = "Password123!", Name = "Test User" }, ct)).EnsureSuccessStatusCode();
        var attempted = $"Wrong-{Guid.NewGuid():N}";

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = attempted }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.Logs.Entries.Should().Contain(e => e.Contains("Login.Command"), "the failure path under test must have logged");
        factory.Logs.Entries.Should().NotContain(e => e.Contains(attempted));
    }

    [Fact]
    public async Task Register_RejectedByValidation_DoesNotLogThePassword()
    {
        var ct = TestContext.Current.CancellationToken;
        var password = $"Rejected-{Guid.NewGuid():N}";

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { Email = "not-an-email", Password = password, Name = "Test User" }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Logs.Entries.Should().NotContain(e => e.Contains(password));
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_DoesNotLogThePassword()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"redact-dup-{Guid.NewGuid()}@example.com";
        (await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { Email = email, Password = "Password123!", Name = "Test User" }, ct)).EnsureSuccessStatusCode();
        var password = $"Duplicate-{Guid.NewGuid():N}";

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { Email = email, Password = password, Name = "Test User" }, ct);

        response.IsSuccessStatusCode.Should().BeFalse();
        factory.Logs.Entries.Should().NotContain(e => e.Contains(password));
    }
}
