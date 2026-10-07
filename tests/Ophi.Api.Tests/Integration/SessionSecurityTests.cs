using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Api.Common.Auth;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Integration;

/// <summary>
/// End-to-end coverage for security-stamp session invalidation: a session cookie issued
/// before a stamp rotation (password change / reset) must stop working.
/// </summary>
public class SessionSecurityTests(OphiWebApplicationFactory factory) : IsolatedIntegrationTest(factory), IClassFixture<OphiWebApplicationFactory>, IDisposable
{
    private readonly OphiWebApplicationFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AuthenticatedRequest_WithCurrentStamp_Succeeds()
    {
        await RegisterAsync("current@example.com");

        var response = await _client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthenticatedRequest_AfterStampRotation_Returns401()
    {
        var userId = await RegisterAsync("rotated@example.com");

        // Establish the session (and the guard's cache), then rotate the stamp out from
        // under it — what ChangePassword will do in UX-5.2.
        var before = await _client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);
        before.StatusCode.Should().Be(HttpStatusCode.OK);

        await RotateStampAsync(userId);

        var after = await _client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PasswordReset_InvalidatesExistingSession()
    {
        var userId = await RegisterAsync("reset-victim@example.com");
        var sessionCheck = await _client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);
        sessionCheck.StatusCode.Should().Be(HttpStatusCode.OK);

        var rawToken = await SeedResetTokenAsync(userId);
        using var anonymousClient = _factory.CreateClient();
        var resetResponse = await anonymousClient.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new { Token = rawToken, NewPassword = "BrandNewPassword1" },
            cancellationToken: TestContext.Current.CancellationToken);
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await _client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> RegisterAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Email = email,
            Password = "Password123!",
            Name = "Session Test User"
        }, cancellationToken: TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        return await db.Users
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task RotateStampAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken);
        user.ChangePassword("rotated-hash");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Mirror what production rotation paths do: prime the guard's cache with the new
        // stamp so the old session dies immediately rather than after the cache TTL.
        _factory.Services.GetRequiredService<SecurityStampGuard>().Refresh(userId, user.SecurityStamp);
    }

    private async Task<string> SeedResetTokenAsync(Guid userId)
    {
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken);
        user.PasswordResetTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(1);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return rawToken;
    }
}
