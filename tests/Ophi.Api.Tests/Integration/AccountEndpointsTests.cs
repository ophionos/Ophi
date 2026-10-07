using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Integration;

/// <summary>
/// End-to-end coverage for the UX-5.2 account endpoints: change password (with session
/// invalidation semantics), update profile (cookie re-issue), and account deletion (cascade).
/// </summary>
public class AccountEndpointsTests(OphiWebApplicationFactory factory) : IsolatedIntegrationTest(factory), IClassFixture<OphiWebApplicationFactory>, IDisposable
{
    private const string Password = "Password123!";

    private readonly OphiWebApplicationFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Change Password

    [Fact]
    public async Task ChangePassword_WithValidCurrentPassword_Returns200AndKeepsSessionAlive()
    {
        await RegisterAsync(_client, "changer@example.com");

        var response = await _client.PutAsJsonAsync("/api/v1/account/password", new
        {
            CurrentPassword = Password,
            NewPassword = "BrandNewPassword1"
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The stamp rotation invalidated the old ticket, but the endpoint re-issues the
        // cookie — the calling session must stay signed in.
        var me = await _client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);
        me.StatusCode.Should().Be(HttpStatusCode.OK);

        // And the new password is the one that works for fresh logins.
        using var freshClient = _factory.CreateClient();
        var login = await freshClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "changer@example.com",
            Password = "BrandNewPassword1"
        }, cancellationToken: TestContext.Current.CancellationToken);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_SignsOutOtherSessions()
    {
        await RegisterAsync(_client, "multi-session@example.com");

        using var otherSession = _factory.CreateClient();
        var otherLogin = await otherSession.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "multi-session@example.com",
            Password = Password
        }, cancellationToken: TestContext.Current.CancellationToken);
        otherLogin.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.PutAsJsonAsync("/api/v1/account/password", new
        {
            CurrentPassword = Password,
            NewPassword = "BrandNewPassword1"
        }, cancellationToken: TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var otherMe = await otherSession.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);
        otherMe.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_Returns401AndChangesNothing()
    {
        await RegisterAsync(_client, "wrong-current@example.com");

        var response = await _client.PutAsJsonAsync("/api/v1/account/password", new
        {
            CurrentPassword = "NotMyPassword1",
            NewPassword = "BrandNewPassword1"
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Original password still valid, session still alive.
        var me = await _client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);
        me.StatusCode.Should().Be(HttpStatusCode.OK);

        using var freshClient = _factory.CreateClient();
        var login = await freshClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "wrong-current@example.com",
            Password
        }, cancellationToken: TestContext.Current.CancellationToken);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WithWeakNewPassword_ReturnsBadRequest()
    {
        await RegisterAsync(_client, "weak-new@example.com");

        var response = await _client.PutAsJsonAsync("/api/v1/account/password", new
        {
            CurrentPassword = Password,
            NewPassword = "weak"
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_WithoutSession_Returns401()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PutAsJsonAsync("/api/v1/account/password", new
        {
            CurrentPassword = Password,
            NewPassword = "BrandNewPassword1"
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Update Profile

    [Fact]
    public async Task UpdateProfile_NameOnly_Returns200AndReflectsInMe()
    {
        await RegisterAsync(_client, "renamer@example.com");

        var response = await _client.PutAsJsonAsync("/api/v1/account/profile", new
        {
            Name = "Renamed User",
            Email = "renamer@example.com"
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // /auth/me reads from cookie claims — the re-issued ticket must carry the new name.
        var me = await _client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", cancellationToken: TestContext.Current.CancellationToken);
        me!.Name.Should().Be("Renamed User");
        me.Email.Should().Be("renamer@example.com");
    }

    [Fact]
    public async Task UpdateProfile_EmailChangeWithoutPassword_ReturnsBadRequest()
    {
        await RegisterAsync(_client, "email-no-pass@example.com");

        var response = await _client.PutAsJsonAsync("/api/v1/account/profile", new
        {
            Name = "Test User",
            Email = "new-address@example.com"
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProfile_EmailChangeWithWrongPassword_Returns401()
    {
        await RegisterAsync(_client, "email-wrong-pass@example.com");

        var response = await _client.PutAsJsonAsync("/api/v1/account/profile", new
        {
            Name = "Test User",
            Email = "new-address@example.com",
            CurrentPassword = "NotMyPassword1"
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateProfile_EmailChangeWithCorrectPassword_Returns200AndMeShowsNewEmail()
    {
        await RegisterAsync(_client, "old-address@example.com");

        var response = await _client.PutAsJsonAsync("/api/v1/account/profile", new
        {
            Name = "Test User",
            Email = "New-Address@Example.com",
            CurrentPassword = Password
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await _client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", cancellationToken: TestContext.Current.CancellationToken);
        me!.Email.Should().Be("new-address@example.com");

        // Login now works with the new email, not the old one.
        using var freshClient = _factory.CreateClient();
        var newLogin = await freshClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "new-address@example.com",
            Password
        }, cancellationToken: TestContext.Current.CancellationToken);
        newLogin.StatusCode.Should().Be(HttpStatusCode.OK);

        var oldLogin = await freshClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "old-address@example.com",
            Password
        }, cancellationToken: TestContext.Current.CancellationToken);
        oldLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateProfile_EmailChangeToTakenEmail_ReturnsConflict()
    {
        using var otherClient = _factory.CreateClient();
        await RegisterAsync(otherClient, "taken@example.com");
        await RegisterAsync(_client, "wants-taken@example.com");

        var response = await _client.PutAsJsonAsync("/api/v1/account/profile", new
        {
            Name = "Test User",
            Email = "taken@example.com",
            CurrentPassword = Password
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateProfile_WithoutSession_Returns401()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PutAsJsonAsync("/api/v1/account/profile", new
        {
            Name = "Test User",
            Email = "anon@example.com"
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Delete Account

    [Fact]
    public async Task DeleteAccount_WithWrongPassword_Returns401AndKeepsAccount()
    {
        await RegisterAsync(_client, "survivor@example.com");

        var response = await DeleteAccountAsync(_client, "NotMyPassword1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var me = await _client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteAccount_WithCorrectPassword_Returns204SignsOutAndCascades()
    {
        var userId = await RegisterAsync(_client, "doomed@example.com");
        await SeedOwnedDataAsync(userId);

        var response = await DeleteAccountAsync(_client, Password);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Session is gone — both the cookie sign-out and the deleted user reject /me.
        var me = await _client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // User and all owned rows cascaded.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        (await db.Users.AnyAsync(u => u.Id == userId, TestContext.Current.CancellationToken)).Should().BeFalse();
        (await db.Products.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.ProductUrls.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.Alerts.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        // Login is impossible afterwards.
        using var freshClient = _factory.CreateClient();
        var login = await freshClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "doomed@example.com",
            Password
        }, cancellationToken: TestContext.Current.CancellationToken);
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteAccount_WithoutSession_Returns401()
    {
        using var anonymous = _factory.CreateClient();

        var response = await DeleteAccountAsync(anonymous, Password);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    private record MeResponse(Guid Id, string Email, string Name);

    private async Task<Guid> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Email = email,
            Password,
            Name = "Test User"
        }, cancellationToken: TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        return await db.Users
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> DeleteAccountAsync(HttpClient client, string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/account")
        {
            Content = JsonContent.Create(new { Password = password })
        };
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task SeedOwnedDataAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var (product, productUrl) = TestEntityFactory.CreateProduct("Doomed Product", userId);
        db.Products.Add(product);
        db.ProductUrls.Add(productUrl);
        db.Alerts.Add(TestEntityFactory.CreateAlert(product.Id, userId, 50m));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
