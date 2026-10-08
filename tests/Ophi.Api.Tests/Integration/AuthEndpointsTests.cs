using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Api.Features.Auth;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Integration;

public class AuthEndpointsTests(OphiWebApplicationFactory factory) : IsolatedIntegrationTest(factory), IClassFixture<OphiWebApplicationFactory>, IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Forgot Password Endpoint Tests

    [Fact]
    public async Task ForgotPassword_WithKnownEmail_StoresResetTokenInTheBackground()
    {
        // The request returns before the lookup; this proves the background message is handled in
        // the API host (not dropped, not routed to a Worker that is not there).
        var ct = TestContext.Current.CancellationToken;
        var email = $"forgot-{Guid.NewGuid()}@example.com";
        (await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { Email = email, Password = "Password123!", Name = "Test User" }, ct)).EnsureSuccessStatusCode();

        var response = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { Email = email }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string? tokenHash = null;
        for (var attempt = 0; attempt < 50 && tokenHash == null; attempt++)
        {
            await Task.Delay(100, ct);
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
            tokenHash = await db.Users.AsNoTracking()
                .Where(u => u.Email == email).Select(u => u.PasswordResetTokenHash).SingleAsync(ct);
        }

        tokenHash.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Register Endpoint Tests

    [Fact]
    public async Task Register_WithValidData_ReturnsCreated()
    {
        // Arrange
        var request = new
        {
            Email = $"test-{Guid.NewGuid()}@example.com",
            Password = "Password123!",
            Name = "Test User"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<Register.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Email.Should().Be(request.Email.ToLower());
        result.Name.Should().Be(request.Name);
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Register_WithInvalidEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            Email = "invalid-email",
            Password = "Password123!",
            Name = "Test User"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WithWeakPassword_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            Email = $"test-{Guid.NewGuid()}@example.com",
            Password = "weak",
            Name = "Test User"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsUnprocessableEntity()
    {
        // Arrange
        var email = $"duplicate-{Guid.NewGuid()}@example.com";
        var firstRequest = new
        {
            Email = email,
            Password = "Password123!",
            Name = "Test User"
        };
        var secondRequest = new
        {
            Email = email,
            Password = "Password456!",
            Name = "Another User"
        };

        // Act - Register first user
        var firstResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", firstRequest, cancellationToken: TestContext.Current.CancellationToken);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act - Try to register second user with same email
        var secondResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", secondRequest, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Register_WithEmptyName_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            Email = $"test-{Guid.NewGuid()}@example.com",
            Password = "Password123!",
            Name = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Login Endpoint Tests

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOkAndSetsCookie()
    {
        // Arrange - First register a user
        var email = $"login-test-{Guid.NewGuid()}@example.com";
        var password = "Password123!";
        var registerRequest = new
        {
            Email = email,
            Password = password,
            Name = "Test User"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest, cancellationToken: TestContext.Current.CancellationToken);

        var loginRequest = new
        {
            Email = email,
            Password = password
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<Login.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Email.Should().Be(email.ToLower());

        // Verify session cookie is set
        response.Headers.Should().ContainKey("Set-Cookie");
    }

    [Fact]
    public async Task Login_WithInvalidEmail_ReturnsUnauthorized()
    {
        // Arrange
        var loginRequest = new
        {
            Email = "nonexistent@example.com",
            Password = "Password123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        // Arrange - First register a user
        var email = $"wrong-pass-{Guid.NewGuid()}@example.com";
        var registerRequest = new
        {
            Email = email,
            Password = "Password123!",
            Name = "Test User"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest, cancellationToken: TestContext.Current.CancellationToken);

        var loginRequest = new
        {
            Email = email,
            Password = "WrongPassword123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithEmptyPassword_ReturnsBadRequest()
    {
        // Arrange
        var loginRequest = new
        {
            Email = "test@example.com",
            Password = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Logout Endpoint Tests

    [Fact]
    public async Task Logout_WhenAuthenticated_ReturnsNoContent()
    {
        // Arrange - Register and login
        var email = $"logout-test-{Guid.NewGuid()}@example.com";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Email = email,
            Password = password,
            Name = "Test User"
        }, cancellationToken: TestContext.Current.CancellationToken);

        await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = email,
            Password = password
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await _client.PostAsync("/api/v1/auth/logout", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region Registration Status Tests

    [Fact]
    public async Task GetRegistrationStatus_ByDefault_ReportsOpenToAnonymousCallers()
    {
        // Registration is open unless an operator sets Registration:Enabled=false.
        var response = await _client.GetAsync("/api/v1/auth/registration", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<GetRegistrationStatus.Response>(TestContext.Current.CancellationToken);
        status!.Open.Should().BeTrue();
    }

    #endregion
}
