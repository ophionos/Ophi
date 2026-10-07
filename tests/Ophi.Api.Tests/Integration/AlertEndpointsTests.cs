using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Api.Features.Alerts;
using Ophi.Api.Features.Products;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Integration;

public class AlertEndpointsTests : IsolatedIntegrationTest, IClassFixture<OphiWebApplicationFactory>, IDisposable
{
    private readonly HttpClient _client;

    public AlertEndpointsTests(OphiWebApplicationFactory factory) : base(factory)
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
        var email = $"alert-test-{Guid.NewGuid()}@example.com";
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

    private static async Task<AddProduct.Response> CreateTestProductAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            Url = $"https://alert-product.com/{Guid.NewGuid()}"
        });
        return (await response.Content.ReadFromJsonAsync<AddProduct.Response>())!;
    }

    #region CreateAlert Endpoint Tests

    [Fact]
    public async Task CreateAlert_WithValidData_ReturnsCreated()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var product = await CreateTestProductAsync(client);
        var request = new
        {
            ProductId = product.Id,
            TargetPrice = 50.00m,
            Condition = "below"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/alerts", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreateAlert.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.ProductId.Should().Be(product.Id);
        result.TargetPrice.Should().Be(50.00m);
        result.Condition.Should().Be("below");
        result.Active.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAlert_WithAboveCondition_ReturnsCreated()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var product = await CreateTestProductAsync(client);
        var request = new
        {
            ProductId = product.Id,
            TargetPrice = 150.00m,
            Condition = "above"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/alerts", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreateAlert.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result!.Condition.Should().Be("above");
    }

    [Fact]
    public async Task CreateAlert_WithPercentDropCondition_ReturnsCreated()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var product = await CreateTestProductAsync(client);
        var request = new
        {
            ProductId = product.Id,
            TargetPrice = 10.00m, // 10% drop
            Condition = "percentDrop"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/alerts", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreateAlert.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result!.Condition.Should().Be("percentDrop");
    }

    [Fact]
    public async Task CreateAlert_WithInvalidProductId_ReturnsNotFound()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var request = new
        {
            ProductId = Guid.NewGuid(),
            TargetPrice = 50.00m,
            Condition = "below"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/alerts", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateAlert_WithNegativeTargetPrice_ReturnsBadRequest()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var product = await CreateTestProductAsync(client);
        var request = new
        {
            ProductId = product.Id,
            TargetPrice = -10.00m,
            Condition = "below"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/alerts", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateAlert_WithInvalidCondition_ReturnsBadRequest()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var product = await CreateTestProductAsync(client);
        var request = new
        {
            ProductId = product.Id,
            TargetPrice = 50.00m,
            Condition = "invalid"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/alerts", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateAlert_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();
        var request = new
        {
            ProductId = Guid.NewGuid(),
            TargetPrice = 50.00m,
            Condition = "below"
        };

        // Act
        var response = await unauthenticatedClient.PostAsJsonAsync("/api/v1/alerts", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region GetAlerts Endpoint Tests

    [Fact]
    public async Task GetAlerts_WithNoAlerts_ReturnsEmptyList()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Act
        var response = await client.GetAsync("/api/v1/alerts", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetAlerts.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAlerts_WithAlerts_ReturnsAlertList()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var product = await CreateTestProductAsync(client);

        // Create some alerts
        await client.PostAsJsonAsync("/api/v1/alerts", new
        {
            ProductId = product.Id,
            TargetPrice = 50.00m,
            Condition = "below"
        }, cancellationToken: TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync("/api/v1/alerts", new
        {
            ProductId = product.Id,
            TargetPrice = 150.00m,
            Condition = "above"
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.GetAsync("/api/v1/alerts", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetAlerts.Response>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAlerts_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/v1/alerts", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region DeleteAlert Endpoint Tests

    [Fact]
    public async Task DeleteAlert_WithValidId_ReturnsNoContent()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var product = await CreateTestProductAsync(client);
        var createResponse = await client.PostAsJsonAsync("/api/v1/alerts", new
        {
            ProductId = product.Id,
            TargetPrice = 50.00m,
            Condition = "below"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var alert = await createResponse.Content.ReadFromJsonAsync<CreateAlert.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.DeleteAsync($"/api/v1/alerts/{alert!.Id}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteAlert_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/api/v1/alerts/{nonExistentId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteAlert_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = Factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.DeleteAsync($"/api/v1/alerts/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region SetAlertActive Endpoint Tests

    [Fact]
    public async Task SetAlertActive_PauseThenResume_RoundTripsThroughGetAlerts()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var product = await CreateTestProductAsync(client);
        var createResponse = await client.PostAsJsonAsync("/api/v1/alerts", new
        {
            ProductId = product.Id,
            TargetPrice = 50.00m,
            Condition = "below"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var alert = await createResponse.Content.ReadFromJsonAsync<CreateAlert.Response>(cancellationToken: TestContext.Current.CancellationToken);

        // Act — pause
        var pause = await client.PatchAsJsonAsync($"/api/v1/alerts/{alert!.Id}", new { Active = false }, TestContext.Current.CancellationToken);

        // Assert
        pause.StatusCode.Should().Be(HttpStatusCode.OK);
        (await pause.Content.ReadFromJsonAsync<GetAlerts.Dto>(TestContext.Current.CancellationToken))!.Active.Should().BeFalse();
        var list = await client.GetFromJsonAsync<GetAlerts.Response>("/api/v1/alerts", TestContext.Current.CancellationToken);
        list!.Items.Single(a => a.Id == alert.Id).Active.Should().BeFalse();

        // Act — resume
        var resume = await client.PatchAsJsonAsync($"/api/v1/alerts/{alert.Id}", new { Active = true }, TestContext.Current.CancellationToken);
        (await resume.Content.ReadFromJsonAsync<GetAlerts.Dto>(TestContext.Current.CancellationToken))!.Active.Should().BeTrue();
    }

    [Fact]
    public async Task SetAlertActive_WithInvalidId_ReturnsNotFound()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PatchAsJsonAsync($"/api/v1/alerts/{Guid.NewGuid()}", new { Active = false }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetAlertActive_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var unauthenticatedClient = Factory.CreateClient();

        var response = await unauthenticatedClient.PatchAsJsonAsync($"/api/v1/alerts/{Guid.NewGuid()}", new { Active = false }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region RedenominateAlert Endpoint Tests

    [Fact]
    public async Task RedenominateAlert_OnADormantAlert_WakesItOnTheNewCurrency()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var alertId = await CreateAlertThenReanchorProductAsync(client, "EUR");

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/alerts/{alertId}/redenominate",
            new { TargetPrice = 69.00m, ExpectedCurrency = "EUR" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RedenominateAlert.Response>(
            cancellationToken: TestContext.Current.CancellationToken);
        result!.Currency.Should().Be("EUR");
        result.TargetPrice.Should().Be(69.00m);
        result.HasCurrencyMismatch.Should().BeFalse();

        // And the alert now reads as live on the list endpoint too, not just in this response.
        var listed = await client.GetFromJsonAsync<GetAlerts.Response>(
            "/api/v1/alerts", cancellationToken: TestContext.Current.CancellationToken);
        listed!.Items.Single(a => a.Id == alertId).HasCurrencyMismatch.Should().BeFalse();
    }

    [Fact]
    public async Task RedenominateAlert_OnALiveAlert_ReturnsUnprocessable()
    {
        // Arrange — a freshly created alert is denominated in its product's currency, so it is live.
        var client = await CreateAuthenticatedClientAsync();
        var product = await CreateTestProductAsync(client);
        var created = await client.PostAsJsonAsync("/api/v1/alerts", new
        {
            ProductId = product.Id,
            TargetPrice = 50.00m,
            Condition = "below"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var alert = await created.Content.ReadFromJsonAsync<CreateAlert.Response>(
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/alerts/{alert!.Id}/redenominate",
            new { TargetPrice = 40.00m, ExpectedCurrency = "USD" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — this endpoint recovers dormant alerts; it is not a back-door target edit.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task RedenominateAlert_WithAStaleExpectedCurrency_ReturnsUnprocessable()
    {
        // Arrange — the page showed EUR, but the product has since re-anchored to GBP.
        var client = await CreateAuthenticatedClientAsync();
        var alertId = await CreateAlertThenReanchorProductAsync(client, "GBP");

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/alerts/{alertId}/redenominate",
            new { TargetPrice = 69.00m, ExpectedCurrency = "EUR" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task RedenominateAlert_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var unauthenticatedClient = Factory.CreateClient();

        var response = await unauthenticatedClient.PostAsJsonAsync(
            $"/api/v1/alerts/{Guid.NewGuid()}/redenominate",
            new { TargetPrice = 10.00m, ExpectedCurrency = "EUR" },
            cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Creates an alert against a product, then re-anchors that product onto
    /// <paramref name="newCurrency"/> directly in the database — the state
    /// <see cref="Ophi.Domain.Services.ProductPriceAggregator"/> produces when no URL is priced in
    /// the product's own currency any more. There is no API surface that re-anchors a product on
    /// demand, so this reaches past HTTP to set up the condition under test.
    /// </summary>
    private async Task<Guid> CreateAlertThenReanchorProductAsync(HttpClient client, string newCurrency)
    {
        var product = await CreateTestProductAsync(client);
        var created = await client.PostAsJsonAsync("/api/v1/alerts", new
        {
            ProductId = product.Id,
            TargetPrice = 50.00m,
            Condition = "below"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var alert = await created.Content.ReadFromJsonAsync<CreateAlert.Response>(
            cancellationToken: TestContext.Current.CancellationToken);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var stored = await db.Products.FirstAsync(
            p => p.Id == product.Id, TestContext.Current.CancellationToken);
        stored.Currency = newCurrency;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return alert!.Id;
    }

    #endregion
}
