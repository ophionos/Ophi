using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Api.Features.Products;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Integration;

public class ExportImportEndpointsTests : IsolatedIntegrationTest, IClassFixture<OphiWebApplicationFactory>
{
    public ExportImportEndpointsTests(OphiWebApplicationFactory factory) : base(factory) { }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync()
    {
        var client = Factory.CreateClient();
        var email = $"export-test-{Guid.NewGuid()}@example.com";
        const string password = "Password123!";

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Email = email,
            Password = password,
            Name = "Test User"
        });

        await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = email,
            Password = password
        });

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == email);

        return (client, user.Id);
    }

    private async Task SeedProductAsync(Guid userId, string name, string url, decimal? price = 29.99m)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var productId = Guid.NewGuid();
        db.Products.Add(new Product
        {
            Id = productId,
            UserId = userId,
            Name = name,
            Currency = "USD",
            CurrentPrice = price,
            Status = ProductStatus.Active
        });
        db.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Url = url,
            Currency = "USD"
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExportCsv_ReturnsProducts()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync();
        await SeedProductAsync(userId, "Export Widget", "https://amazon.com/dp/BEXPORT1");

        var response = await client.GetAsync("/api/v1/products/export", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Contain("name,url,current_price");
        content.Should().Contain("Export Widget");
    }

    [Fact]
    public async Task ExportJson_ReturnsJsonArray()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync();
        await SeedProductAsync(userId, "JSON Widget", "https://amazon.com/dp/BJSON1");

        var response = await client.GetAsync("/api/v1/products/export?format=json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<ExportProducts.ExportRow>>(TestContext.Current.CancellationToken);
        rows.Should().NotBeNull();
        rows!.Should().Contain(r => r.Name == "JSON Widget");
    }

    [Fact]
    public async Task ExportCsv_WithoutAuth_ReturnsUnauthorized()
    {
        using var unauthClient = Factory.CreateClient();
        var response = await unauthClient.GetAsync("/api/v1/products/export", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ImportCsv_AddsNewProducts()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();

        var csv = "url,name,target_price,tags\nhttps://amazon.com/dp/BIMP001,Import Widget,25.00,electronics";
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "products.csv");

        var response = await client.PostAsync("/api/v1/products/import", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportProducts.ImportResponse>(TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result!.Added.Should().Be(1);
        result.Skipped.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportCsv_SkipsDuplicateUrls()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync();
        await SeedProductAsync(userId, "Existing", "https://amazon.com/dp/BDUP001");

        var csv = "url,name\nhttps://amazon.com/dp/BDUP001,Duplicate";
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "products.csv");

        var response = await client.PostAsync("/api/v1/products/import", content, TestContext.Current.CancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ImportProducts.ImportResponse>(TestContext.Current.CancellationToken);
        result!.Skipped.Should().Be(1);
        result.Added.Should().Be(0);
    }

    [Fact]
    public async Task ImportCsv_ReportsInvalidUrls()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();

        var csv = "url\nnot-a-url";
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "products.csv");

        var response = await client.PostAsync("/api/v1/products/import", content, TestContext.Current.CancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ImportProducts.ImportResponse>(TestContext.Current.CancellationToken);
        result!.Errors.Should().HaveCount(1);
        result.Errors[0].Should().Contain("invalid URL");
    }

    [Fact]
    public async Task ImportCsv_RequiresUrlColumn()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();

        var csv = "name,price\nWidget,10.00";
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "products.csv");

        var response = await client.PostAsync("/api/v1/products/import", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ImportCsv_WithoutAuth_ReturnsUnauthorized()
    {
        using var unauthClient = Factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent("url\nhttps://example.com"u8.ToArray()), "file", "products.csv");

        var response = await unauthClient.PostAsync("/api/v1/products/import", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
