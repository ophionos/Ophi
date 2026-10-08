using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Api.Features.ApiKeys;

namespace Ophi.Api.Tests.Integration;

public class ApiKeyEndpointsTests : IsolatedIntegrationTest, IClassFixture<OphiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiKeyEndpointsTests(OphiWebApplicationFactory factory) : base(factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var email = $"apikey-test-{Guid.NewGuid()}@example.com";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Email = email,
            Password = password,
            Name = "Test User"
        });

        await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = email,
            Password = password
        });

        return _client;
    }

    [Fact]
    public async Task CreateApiKey_ReturnsCreatedWithRawKey()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/api-keys", new
        {
            Name = "Test Key",
            Scopes = new[] { "read" }
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreateApiKey.Response>(TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result!.Key.Should().StartWith("ophi_");
        result.Name.Should().Be("Test Key");
        result.Scopes.Should().Contain("read");
    }

    [Fact]
    public async Task ListApiKeys_ReturnsCreatedKeys()
    {
        var client = await CreateAuthenticatedClientAsync();

        // Create a key first
        await client.PostAsJsonAsync("/api/v1/api-keys", new
        {
            Name = "List Test Key",
            Scopes = new[] { "read", "write" }
        }, TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/api/v1/api-keys", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var keys = await response.Content.ReadFromJsonAsync<List<ListApiKeys.Response>>(TestContext.Current.CancellationToken);
        keys.Should().NotBeNull();
        keys!.Should().Contain(k => k.Name == "List Test Key");
    }

    [Fact]
    public async Task DeleteApiKey_ReturnsNoContent()
    {
        var client = await CreateAuthenticatedClientAsync();

        // Create then delete
        var createResponse = await client.PostAsJsonAsync("/api/v1/api-keys", new
        {
            Name = "Delete Me",
            Scopes = new[] { "read" }
        }, TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKey.Response>(TestContext.Current.CancellationToken);

        var response = await client.DeleteAsync($"/api/v1/api-keys/{created!.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ApiKeyAuth_CanAccessProtectedEndpoint()
    {
        var client = await CreateAuthenticatedClientAsync();

        // Create an API key
        var createResponse = await client.PostAsJsonAsync("/api/v1/api-keys", new
        {
            Name = "Auth Test Key",
            Scopes = new[] { "read" }
        }, TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKey.Response>(TestContext.Current.CancellationToken);

        // Use a fresh client without cookies, authenticate via API key
        using var apiClient = Factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", created!.Key);

        var response = await apiClient.GetAsync("/api/v1/products", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiKeyAuth_ReadOnlyKey_CannotMutate()
    {
        // Scopes were validated, stored and shown in the UI but never checked, so a key the user
        // created as read-only had full write access — including DELETE on their products.
        using var apiClient = await CreateApiKeyClientAsync(["read"]);

        var response = await apiClient.PostAsJsonAsync("/api/v1/tags", new
        {
            Name = $"scope-test-{Guid.NewGuid()}"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ApiKeyAuth_ReadOnlyKey_CanStillRead()
    {
        using var apiClient = await CreateApiKeyClientAsync(["read"]);

        var response = await apiClient.GetAsync("/api/v1/tags", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiKeyAuth_WriteKey_CanMutate()
    {
        // Guard against over-correcting: a key that does carry "write" must still work.
        using var apiClient = await CreateApiKeyClientAsync(["read", "write"]);

        var response = await apiClient.PostAsJsonAsync("/api/v1/tags", new
        {
            Name = $"scope-test-{Guid.NewGuid()}"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ApiKeyAuth_WriteKey_CannotCreateApiKey()
    {
        // A leaked key must not mint a fresh, non-expiring key: that would outlive the leaked
        // key's expiry and its revocation. Key management is cookie-session only.
        using var apiClient = await CreateApiKeyClientAsync(["read", "write"]);

        var response = await apiClient.PostAsJsonAsync("/api/v1/api-keys", new
        {
            Name = "minted",
            Scopes = new[] { "read", "write" }
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ApiKeyAuth_WriteKey_CannotDeleteApiKey()
    {
        using var apiClient = await CreateApiKeyClientAsync(["read", "write"]);

        var response = await apiClient.DeleteAsync($"/api/v1/api-keys/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CookieSession_IsUnaffectedByScopeEnforcement()
    {
        // Cookie principals carry no scopes claim and must never be restricted by it.
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/tags", new
        {
            Name = $"scope-test-{Guid.NewGuid()}"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<HttpClient> CreateApiKeyClientAsync(string[] scopes)
    {
        var client = await CreateAuthenticatedClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/api-keys", new
        {
            Name = $"Scope Test Key {Guid.NewGuid()}",
            Scopes = scopes
        }, TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKey.Response>(TestContext.Current.CancellationToken);

        // Fresh client so only the bearer key authenticates — no cookie fallback.
        var apiClient = Factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", created!.Key);
        return apiClient;
    }

    [Fact]
    public async Task ApiKeyAuth_InvalidKey_ReturnsUnauthorized()
    {
        using var apiClient = Factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "ophi_invalid_key_here");

        var response = await apiClient.GetAsync("/api/v1/products", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiKeyAuth_ExpiredKey_ReturnsUnauthorized()
    {
        var client = await CreateAuthenticatedClientAsync();

        // Create an already-expired key by creating normally then manipulating DB
        var createResponse = await client.PostAsJsonAsync("/api/v1/api-keys", new
        {
            Name = "Expired Key",
            Scopes = new[] { "read" },
            ExpiresAt = DateTime.UtcNow.AddDays(1) // Create with future date first
        }, TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKey.Response>(TestContext.Current.CancellationToken);

        // Manually expire it in the DB via EF tracking
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Ophi.Infrastructure.Persistence.OphiDbContext>();
            var key = await db.ApiKeys.FirstAsync(k => k.Id == created!.Id, TestContext.Current.CancellationToken);
            key.ExpiresAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var apiClient = Factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", created!.Key);

        var response = await apiClient.GetAsync("/api/v1/products", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateApiKey_WithoutAuth_ReturnsUnauthorized()
    {
        using var unauthClient = Factory.CreateClient();
        var response = await unauthClient.PostAsJsonAsync("/api/v1/api-keys", new
        {
            Name = "Should Fail",
            Scopes = new[] { "read" }
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateApiKey_InvalidScopes_ReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/api-keys", new
        {
            Name = "Bad Scopes",
            Scopes = new[] { "admin" }
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ApiKeyAuth_LastUsedAt_IsThrottled_WithinOneMinute()
    {
        var client = await CreateAuthenticatedClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/api-keys", new
        {
            Name = "Throttle Test Key",
            Scopes = new[] { "read" }
        }, TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKey.Response>(TestContext.Current.CancellationToken);

        // Reset the per-process throttle cache so the first request below is observed as fresh.
        Ophi.Api.Common.Auth.ApiKeyAuthenticationHandler.ResetLastUsedCacheForTests();

        using var apiClient = Factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", created!.Key);

        // First request — writes LastUsedAt
        (await apiClient.GetAsync("/api/v1/products", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        DateTime? firstWrite;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Ophi.Infrastructure.Persistence.OphiDbContext>();
            firstWrite = await db.ApiKeys
                .Where(k => k.Id == created.Id)
                .Select(k => k.LastUsedAt)
                .FirstAsync(TestContext.Current.CancellationToken);
        }
        firstWrite.Should().NotBeNull();

        // Second request — within throttle window, should NOT write again
        (await apiClient.GetAsync("/api/v1/products", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        DateTime? secondWrite;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Ophi.Infrastructure.Persistence.OphiDbContext>();
            secondWrite = await db.ApiKeys
                .Where(k => k.Id == created.Id)
                .Select(k => k.LastUsedAt)
                .FirstAsync(TestContext.Current.CancellationToken);
        }

        secondWrite.Should().Be(firstWrite, "second request within throttle window must not re-write LastUsedAt");
    }
}
