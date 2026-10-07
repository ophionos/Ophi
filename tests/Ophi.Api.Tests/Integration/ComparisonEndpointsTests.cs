using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Ophi.Api.Features.Comparisons;

namespace Ophi.Api.Tests.Integration;

public class ComparisonEndpointsTests : IsolatedIntegrationTest, IClassFixture<OphiWebApplicationFactory>, IDisposable
{
    private readonly HttpClient _client;

    public ComparisonEndpointsTests(OphiWebApplicationFactory factory) : base(factory)
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
        var email = $"comparison-test-{Guid.NewGuid()}@example.com";
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

    private static async Task<Guid> CreateProductAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            Url = $"https://example.com/product/{Guid.NewGuid()}"
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProductResponse>();
        return result!.Id;
    }

    private record ProductResponse(Guid Id, string Name, string Url, string Currency, string Status);

    #region CreateComparisonGroup Endpoint Tests

    [Fact]
    public async Task CreateComparisonGroup_WithValidName_ReturnsCreated()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var request = new { Name = $"Headphones Comparison {Guid.NewGuid()}" };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/comparisons", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreateComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Name.Should().Be(request.Name);
        result.ProductCount.Should().Be(0);
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateComparisonGroup_WithDescription_ReturnsCreatedWithDescription()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var request = new
        {
            Name = $"Headphones Comparison {Guid.NewGuid()}",
            Description = "Compare Sony, Bose, and Apple headphones"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/comparisons", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateComparisonGroup_WithEmptyName_ReturnsBadRequest()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var request = new { Name = "" };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/comparisons", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateComparisonGroup_WithDuplicateName_ReturnsConflict()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var name = $"Duplicate Group {Guid.NewGuid()}";
        var request = new { Name = name };

        // Create first group
        var firstResponse = await client.PostAsJsonAsync("/api/v1/comparisons", request, cancellationToken: TestContext.Current.CancellationToken);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act - Try to create second group with same name
        var secondResponse = await client.PostAsJsonAsync("/api/v1/comparisons", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateComparisonGroup_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();
        var request = new { Name = "Test Group" };

        // Act
        var response = await unauthenticatedClient.PostAsJsonAsync("/api/v1/comparisons", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region GetComparisonGroups Endpoint Tests

    [Fact]
    public async Task GetComparisonGroups_WithNoGroups_ReturnsEmptyList()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Act
        var response = await client.GetAsync("/api/v1/comparisons", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetComparisonGroups.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetComparisonGroups_WithGroups_ReturnsGroupCollection()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Create some groups
        await client.PostAsJsonAsync("/api/v1/comparisons", new { Name = $"Group A {Guid.NewGuid()}" }, cancellationToken: TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync("/api/v1/comparisons", new { Name = $"Group B {Guid.NewGuid()}" }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.GetAsync("/api/v1/comparisons", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetComparisonGroups.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetComparisonGroups_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/v1/comparisons", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region GetComparisonGroup Endpoint Tests

    [Fact]
    public async Task GetComparisonGroup_WithValidId_ReturnsGroupDetails()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/v1/comparisons", new
        {
            Name = $"Test Group {Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.GetAsync($"/api/v1/comparisons/{createResult!.Id}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Id.Should().Be(createResult.Id);
        result.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task GetComparisonGroup_WithProducts_ReturnsBestPrice()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Create group
        var createResponse = await client.PostAsJsonAsync("/api/v1/comparisons", new
        {
            Name = $"Price Test Group {Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var groupResult = await createResponse.Content.ReadFromJsonAsync<CreateComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Create and add products to group
        var productId1 = await CreateProductAsync(client);
        var productId2 = await CreateProductAsync(client);

        await client.PostAsJsonAsync($"/api/v1/comparisons/{groupResult!.Id}/products", new { ProductId = productId1 }, cancellationToken: TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync($"/api/v1/comparisons/{groupResult.Id}/products", new { ProductId = productId2 }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.GetAsync($"/api/v1/comparisons/{groupResult.Id}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Products.Should().HaveCount(2);
        // Products are in pending state with no prices (scraping happens asynchronously),
        // so BestPrice is null and no product is marked as best price
        result.BestPrice.Should().BeNull();
        result.Products.Should().NotContain(p => p.IsBestPrice);
    }

    [Fact]
    public async Task GetComparisonGroup_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Act
        var response = await client.GetAsync($"/api/v1/comparisons/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetComparisonGroup_WithDaysParameter_ReturnsOk()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/v1/comparisons", new
        {
            Name = $"Test Group {Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.GetAsync($"/api/v1/comparisons/{createResult!.Id}?days=60", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetComparisonGroup_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.GetAsync($"/api/v1/comparisons/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region DeleteComparisonGroup Endpoint Tests

    [Fact]
    public async Task DeleteComparisonGroup_WithValidId_ReturnsNoContent()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/v1/comparisons", new
        {
            Name = $"To Delete {Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.DeleteAsync($"/api/v1/comparisons/{createResult!.Id}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await client.GetAsync($"/api/v1/comparisons/{createResult.Id}", TestContext.Current.CancellationToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteComparisonGroup_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Act
        var response = await client.DeleteAsync($"/api/v1/comparisons/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteComparisonGroup_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.DeleteAsync($"/api/v1/comparisons/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region AddProductToGroup Endpoint Tests

    [Fact]
    public async Task AddProductToGroup_WithValidData_ReturnsNoContent()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Create group
        var createGroupResponse = await client.PostAsJsonAsync("/api/v1/comparisons", new
        {
            Name = $"Add Product Test {Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var groupResult = await createGroupResponse.Content.ReadFromJsonAsync<CreateComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Create product
        var productId = await CreateProductAsync(client);

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/comparisons/{groupResult!.Id}/products", new
        {
            ProductId = productId
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify product is in group
        var getResponse = await client.GetAsync($"/api/v1/comparisons/{groupResult.Id}", TestContext.Current.CancellationToken);
        var getResult = await getResponse.Content.ReadFromJsonAsync<GetComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);
        getResult!.Products.Should().Contain(p => p.Id == productId);
    }

    [Fact]
    public async Task AddProductToGroup_WithNonExistentGroup_ReturnsNotFound()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var productId = await CreateProductAsync(client);

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/comparisons/{Guid.NewGuid()}/products", new
        {
            ProductId = productId
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddProductToGroup_WithNonExistentProduct_ReturnsNotFound()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var createGroupResponse = await client.PostAsJsonAsync("/api/v1/comparisons", new
        {
            Name = $"Test Group {Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var groupResult = await createGroupResponse.Content.ReadFromJsonAsync<CreateComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/comparisons/{groupResult!.Id}/products", new
        {
            ProductId = Guid.NewGuid()
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddProductToGroup_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.PostAsJsonAsync($"/api/v1/comparisons/{Guid.NewGuid()}/products", new
        {
            ProductId = Guid.NewGuid()
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region AddProductsToGroup (batch) Endpoint Tests

    [Fact]
    public async Task AddProductsToGroup_WithMultipleProducts_ReturnsNoContentAndAddsAll()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var createGroupResponse = await client.PostAsJsonAsync("/api/v1/comparisons", new
        {
            Name = $"Batch Add Test {Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var groupResult = await createGroupResponse.Content.ReadFromJsonAsync<CreateComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);

        var productId1 = await CreateProductAsync(client);
        var productId2 = await CreateProductAsync(client);

        // Act — exercises routing + JSON body binding ({ productIds: [...] }) through the full pipeline
        var response = await client.PostAsJsonAsync($"/api/v1/comparisons/{groupResult!.Id}/products/batch", new
        {
            ProductIds = new[] { productId1, productId2 }
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"/api/v1/comparisons/{groupResult.Id}", TestContext.Current.CancellationToken);
        var getResult = await getResponse.Content.ReadFromJsonAsync<GetComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);
        getResult!.Products.Should().Contain(p => p.Id == productId1);
        getResult.Products.Should().Contain(p => p.Id == productId2);
    }

    [Fact]
    public async Task AddProductsToGroup_WithNonExistentGroup_ReturnsNotFound()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var productId = await CreateProductAsync(client);

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/comparisons/{Guid.NewGuid()}/products/batch", new
        {
            ProductIds = new[] { productId }
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddProductsToGroup_WithEmptyProductIds_ReturnsBadRequest()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var createGroupResponse = await client.PostAsJsonAsync("/api/v1/comparisons", new
        {
            Name = $"Batch Empty Test {Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var groupResult = await createGroupResponse.Content.ReadFromJsonAsync<CreateComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/comparisons/{groupResult!.Id}/products/batch", new
        {
            ProductIds = Array.Empty<Guid>()
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddProductsToGroup_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.PostAsJsonAsync($"/api/v1/comparisons/{Guid.NewGuid()}/products/batch", new
        {
            ProductIds = new[] { Guid.NewGuid() }
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region RemoveProductFromGroup Endpoint Tests

    [Fact]
    public async Task RemoveProductFromGroup_WithValidData_ReturnsNoContent()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Create group
        var createGroupResponse = await client.PostAsJsonAsync("/api/v1/comparisons", new
        {
            Name = $"Remove Product Test {Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var groupResult = await createGroupResponse.Content.ReadFromJsonAsync<CreateComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Create and add product
        var productId = await CreateProductAsync(client);
        await client.PostAsJsonAsync($"/api/v1/comparisons/{groupResult!.Id}/products", new { ProductId = productId }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.DeleteAsync($"/api/v1/comparisons/{groupResult.Id}/products/{productId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify product is removed
        var getResponse = await client.GetAsync($"/api/v1/comparisons/{groupResult.Id}", TestContext.Current.CancellationToken);
        var getResult = await getResponse.Content.ReadFromJsonAsync<GetComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);
        getResult!.Products.Should().NotContain(p => p.Id == productId);
    }

    [Fact]
    public async Task RemoveProductFromGroup_WithNonExistentGroup_ReturnsNotFound()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Act
        var response = await client.DeleteAsync($"/api/v1/comparisons/{Guid.NewGuid()}/products/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemoveProductFromGroup_WithProductNotInGroup_ReturnsBadRequest()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Create group
        var createGroupResponse = await client.PostAsJsonAsync("/api/v1/comparisons", new
        {
            Name = $"Test Group {Guid.NewGuid()}"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var groupResult = await createGroupResponse.Content.ReadFromJsonAsync<CreateComparisonGroup.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Create product but DON'T add it to the group
        var productId = await CreateProductAsync(client);

        // Act
        var response = await client.DeleteAsync($"/api/v1/comparisons/{groupResult!.Id}/products/{productId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RemoveProductFromGroup_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.DeleteAsync($"/api/v1/comparisons/{Guid.NewGuid()}/products/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion
}
