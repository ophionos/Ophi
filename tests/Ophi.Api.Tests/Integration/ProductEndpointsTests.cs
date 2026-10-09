using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Ophi.Api.Features.Products;


namespace Ophi.Api.Tests.Integration;

public class ProductEndpointsTests : IsolatedIntegrationTest, IClassFixture<OphiWebApplicationFactory>, IDisposable
{
    private readonly HttpClient _client;

    public ProductEndpointsTests(OphiWebApplicationFactory factory) : base(factory)
    {
        _client = Factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var email = $"product-test-{Guid.NewGuid()}@example.com";
        const string password = "Password123!";

        // Register
        await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Email = email,
            Password = password,
            Name = "Test User"
        });

        // Login
        await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = email,
            Password = password
        });

        return _client;
    }

    #region AddProduct Endpoint Tests

    [Fact]
    public async Task AddProduct_WithValidUrl_ReturnsAccepted()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var request = new { Url = "https://amazon.com/dp/B09TEST123" };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/products", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var result = await response.Content.ReadFromJsonAsync<AddProduct.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Url.Should().Be(request.Url);
        // Product is initially in pending state - scraping happens asynchronously
        result.Name.Should().Be("Loading...");
        result.Status.Should().Be("pending");
        result.CurrentPrice.Should().BeNull();
    }

    [Fact]
    public async Task AddProduct_WithInvalidUrl_ReturnsBadRequest()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var request = new { Url = "not-a-valid-url" };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/products", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddProduct_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange - Create a fresh client without authentication
        using var unauthenticatedClient = Factory.CreateClient();
        var request = new { Url = "https://amazon.com/dp/B09TEST123" };

        // Act
        var response = await unauthenticatedClient.PostAsJsonAsync("/api/v1/products", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddProduct_WithDuplicateUrl_ReturnsConflict()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var request = new { Url = $"https://amazon.com/dp/B09DUP{Guid.NewGuid():N}"[..50] };

        // Add product first time
        var firstResponse = await client.PostAsJsonAsync("/api/v1/products", request, cancellationToken: TestContext.Current.CancellationToken);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Act - Try to add same product again
        var secondResponse = await client.PostAsJsonAsync("/api/v1/products", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AddProduct_WithDuplicateUrlByKey_ReturnsConflictWithTheExistingProductId()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var url = $"https://shop.example/p/{Guid.NewGuid():N}";
        var first = await client.PostAsJsonAsync("/api/v1/products", new { Url = url }, cancellationToken: TestContext.Current.CancellationToken);
        var created = await first.Content.ReadFromJsonAsync<AddProduct.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var second = await client.PostAsJsonAsync("/api/v1/products", new { Url = url + "?utm_source=extension" }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        body.GetProperty("error").GetString().Should().Be("Conflict");
        body.GetProperty("productId").GetGuid().Should().Be(created!.Id);
        body.TryGetProperty("productUrlId", out _).Should().BeTrue();
        // details stays the field → messages map the frontend reads; the IDs must not leak into it.
        body.TryGetProperty("details", out var details).Should().BeTrue();
        details.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task LookupProduct_WithTrackedUrl_ReturnsTheProduct()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var url = $"https://shop.example/p/{Guid.NewGuid():N}";
        var first = await client.PostAsJsonAsync("/api/v1/products", new { Url = url }, cancellationToken: TestContext.Current.CancellationToken);
        var created = await first.Content.ReadFromJsonAsync<AddProduct.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.GetAsync(
            $"/api/v1/products/lookup?url={Uri.EscapeDataString(url + "?fbclid=abc")}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LookupProduct.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result!.ProductId.Should().Be(created!.Id);
    }

    [Fact]
    public async Task LookupProduct_WithUntrackedUrl_ReturnsNotFound()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            $"/api/v1/products/lookup?url={Uri.EscapeDataString("https://shop.example/p/never-added")}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region GetProducts Endpoint Tests

    [Fact]
    public async Task GetProducts_WithNoProducts_ReturnsEmptyList()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Act
        var response = await client.GetAsync("/api/v1/products", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetProducts.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task GetProducts_WithProducts_ReturnsProductList()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Add some products
        await client.PostAsJsonAsync("/api/v1/products", new { Url = $"https://test1.com/{Guid.NewGuid()}" }, cancellationToken: TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync("/api/v1/products", new { Url = $"https://test2.com/{Guid.NewGuid()}" }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.GetAsync("/api/v1/products", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetProducts.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(2);
    }

    [Fact]
    public async Task GetProducts_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/v1/products", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region DeleteProduct Endpoint Tests

    [Fact]
    public async Task DeleteProduct_WithValidId_ReturnsNoContent()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var addResponse = await client.PostAsJsonAsync("/api/v1/products", new
        {
            Url = $"https://delete-test.com/{Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var product = await addResponse.Content.ReadFromJsonAsync<AddProduct.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.DeleteAsync($"/api/v1/products/{product!.Id}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteProduct_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/api/v1/products/{nonExistentId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProduct_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.DeleteAsync($"/api/v1/products/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region GetPriceHistory Endpoint Tests

    [Fact]
    public async Task GetPriceHistory_WithValidProduct_ReturnsHistory()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var addResponse = await client.PostAsJsonAsync("/api/v1/products", new
        {
            Url = $"https://history-test.com/{Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var product = await addResponse.Content.ReadFromJsonAsync<AddProduct.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.GetAsync($"/api/v1/products/{product!.Id}/history", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPriceHistory_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/v1/products/{nonExistentId}/history", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPriceHistory_WithLargeDaysParam_ReturnsOkWithCappedResult()
    {
        // Arrange — days=999999 should be clamped to 365, not cause an error
        var client = await CreateAuthenticatedClientAsync();
        var addResponse = await client.PostAsJsonAsync("/api/v1/products", new
        {
            Url = $"https://largedays-test.com/{Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var product = await addResponse.Content.ReadFromJsonAsync<AddProduct.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.GetAsync($"/api/v1/products/{product!.Id}/history?days=999999", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region GetScrapeLog Endpoint Tests

    [Fact]
    public async Task GetScrapeLog_WithLargeLimitParam_ReturnsOkWithCappedResult()
    {
        // Arrange — limit=100000 should be clamped to 200, not cause an error
        var client = await CreateAuthenticatedClientAsync();
        var addResponse = await client.PostAsJsonAsync("/api/v1/products", new
        {
            Url = $"https://largelimit-test.com/{Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var product = await addResponse.Content.ReadFromJsonAsync<AddProduct.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.GetAsync($"/api/v1/products/{product!.Id}/scrape-log?limit=100000", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}
