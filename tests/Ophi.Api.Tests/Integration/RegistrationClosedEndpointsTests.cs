using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Auth;

namespace Ophi.Api.Tests.Integration;

/// <summary>
/// Variant of <see cref="OphiWebApplicationFactory"/> running with <c>Registration:Enabled=false</c>,
/// the setting an internet-facing instance uses to stop strangers signing up.
/// </summary>
public class RegistrationClosedFactory : OphiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Registration:Enabled", "false");
    }
}

public class RegistrationClosedEndpointsTests(RegistrationClosedFactory factory)
    : IsolatedIntegrationTest(factory), IClassFixture<RegistrationClosedFactory>, IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<HttpResponseMessage> RegisterAsync(string email) =>
        _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Email = email,
            Password = "Password123!",
            Name = "Test User"
        }, cancellationToken: TestContext.Current.CancellationToken);

    private async Task<GetRegistrationStatus.Response?> GetStatusAsync() =>
        await _client.GetFromJsonAsync<GetRegistrationStatus.Response>(
            "/api/v1/auth/registration", TestContext.Current.CancellationToken);

    [Fact]
    public async Task Register_WhenClosedAndNoAccountExists_CreatesTheFirstAccount()
    {
        var response = await RegisterAsync("owner@example.com");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Register_WhenClosedAndAnAccountExists_ReturnsForbidden()
    {
        (await RegisterAsync("owner@example.com")).EnsureSuccessStatusCode();

        var response = await RegisterAsync("stranger@example.com");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        error!.Error.Should().Be("RegistrationDisabled");
    }

    [Fact]
    public async Task GetRegistrationStatus_WhenClosed_ReportsOpenOnlyUntilTheFirstAccountExists()
    {
        (await GetStatusAsync())!.Open.Should().BeTrue();

        (await RegisterAsync("owner@example.com")).EnsureSuccessStatusCode();

        (await GetStatusAsync())!.Open.Should().BeFalse();
    }
}
