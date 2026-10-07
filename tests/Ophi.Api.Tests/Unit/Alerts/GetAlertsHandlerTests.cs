using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Features.Alerts;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Alerts;

public class GetAlertsHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetAlerts.Handler _handler;
    private readonly Guid _testUserId = Guid.NewGuid();

    public GetAlertsHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetAlerts.Handler(_dbContext, NullLogger<GetAlerts.Handler>.Instance);

        // Seed test user for FK constraints
        var testUser = new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(testUser);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Handle_WithNoAlerts_ReturnsEmptyList()
    {
        // Arrange
        var query = new GetAlerts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithAlerts_ReturnsUserAlerts()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        var alert = CreateAlert(product.Id, 50m, AlertCondition.Below);
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetAlerts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be(alert.Id);
    }

    [Fact]
    public async Task Handle_ReturnsCorrectAlertData()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        product.CurrentPrice = 75m;
        _dbContext.Products.Add(product);

        var alert = CreateAlert(product.Id, 50m, AlertCondition.Below);
        alert.IsActive = true;
        alert.LastTriggeredAt = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetAlerts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        var alertDto = result.Items[0];
        alertDto.ProductId.Should().Be(product.Id);
        alertDto.ProductName.Should().Be("Test Product");
        alertDto.CurrentPrice.Should().Be(75m);
        alertDto.TargetPrice.Should().Be(50m);
        alertDto.Condition.Should().Be("below");
        alertDto.Active.Should().BeTrue();
        alertDto.LastTriggered.Should().Be(new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Handle_OnlyReturnsAlertsForRequestedUser()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        var otherUser = new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            Name = "Other User",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(otherUser);

        var myProduct = CreateProduct("My Product");
        _dbContext.Products.Add(myProduct);

        var otherProduct = new Product
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(otherProduct);

        var myAlert = CreateAlert(myProduct.Id, 50m, AlertCondition.Below);
        var otherAlert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = otherProduct.Id,
            UserId = otherUserId,
            TargetPrice = 100m,
            Condition = AlertCondition.Below,
            IsActive = true
        };

        _dbContext.Alerts.AddRange(myAlert, otherAlert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetAlerts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be(myAlert.Id);
    }

    [Fact]
    public async Task Handle_ReturnsAlertsOrderedByCreatedAtDescending()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        var oldAlert = CreateAlert(product.Id, 50m, AlertCondition.Below);
        oldAlert.CreatedAt = DateTime.UtcNow.AddDays(-10);

        var newAlert = CreateAlert(product.Id, 75m, AlertCondition.Above);
        newAlert.CreatedAt = DateTime.UtcNow;

        _dbContext.Alerts.AddRange(oldAlert, newAlert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetAlerts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items[0].TargetPrice.Should().Be(75m);
        result.Items[1].TargetPrice.Should().Be(50m);
    }

    [Theory]
    [InlineData(AlertCondition.Below, "below")]
    [InlineData(AlertCondition.Above, "above")]
    [InlineData(AlertCondition.PercentDrop, "percentDrop")]
    public async Task Handle_MapsConditionToApiString(AlertCondition condition, string expected)
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        var alert = CreateAlert(product.Id, 50m, condition);
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetAlerts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].Condition.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_WithInactiveAlert_ReturnsCorrectActiveStatus()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        var alert = CreateAlert(product.Id, 50m, AlertCondition.Below);
        alert.IsActive = false;
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetAlerts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].Active.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithNullLastTriggered_ReturnsNullLastTriggered()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        var alert = CreateAlert(product.Id, 50m, AlertCondition.Below);
        alert.LastTriggeredAt = null;
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetAlerts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].LastTriggered.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithProductWithNullCurrentPrice_ReturnsNullCurrentPrice()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        product.CurrentPrice = null;
        _dbContext.Products.Add(product);

        var alert = CreateAlert(product.Id, 50m, AlertCondition.Below);
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetAlerts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].CurrentPrice.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenAlertCurrencyDiffersFromProduct_FlagsTheAlertAsMismatched()
    {
        // The client needs to know the alert is dormant, and needs the alert's own currency to
        // format the target with — rendering a USD target next to the product's EUR label is how
        // this reads as "€80" to a user who set 80 dollars.
        var product = TestEntityFactory.Product(_testUserId).Named("Re-anchored").WithCurrency("EUR").Build();
        _dbContext.Products.Add(product);

        var alert = TestEntityFactory.Alert(product.Id, _testUserId)
            .WithTarget(80m).WithCondition(AlertCondition.Below).WithCurrency("USD").Build();
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _handler.Handle(new GetAlerts.Query(_testUserId), TestContext.Current.CancellationToken);

        var dto = result.Items.Should().ContainSingle().Subject;
        dto.Currency.Should().Be("USD");
        dto.ProductCurrency.Should().Be("EUR");
        dto.HasCurrencyMismatch.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenCurrenciesAgree_DoesNotFlagAMismatch()
    {
        var product = TestEntityFactory.Product(_testUserId).Named("Normal").WithCurrency("EUR").Build();
        _dbContext.Products.Add(product);

        var alert = TestEntityFactory.Alert(product.Id, _testUserId)
            .WithTarget(80m).WithCondition(AlertCondition.Below).WithCurrency("EUR").Build();
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _handler.Handle(new GetAlerts.Query(_testUserId), TestContext.Current.CancellationToken);

        result.Items.Should().ContainSingle().Which.HasCurrencyMismatch.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PercentDropAlert_IsNeverFlaggedAsMismatched()
    {
        // Percent-drop targets are percentages, so they survive a re-denomination and must not be
        // shown to the user as dormant.
        var product = TestEntityFactory.Product(_testUserId).Named("Re-anchored").WithCurrency("EUR").Build();
        _dbContext.Products.Add(product);

        var alert = TestEntityFactory.Alert(product.Id, _testUserId)
            .WithTarget(20m).WithCondition(AlertCondition.PercentDrop).WithCurrency("USD").Build();
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _handler.Handle(new GetAlerts.Query(_testUserId), TestContext.Current.CancellationToken);

        result.Items.Should().ContainSingle().Which.HasCurrencyMismatch.Should().BeFalse();
    }

    private Product CreateProduct(string name) =>
        TestEntityFactory.Product(_testUserId).Named(name).Build();

    private Alert CreateAlert(Guid productId, decimal targetPrice, AlertCondition condition) =>
        TestEntityFactory.Alert(productId, _testUserId).WithTarget(targetPrice).WithCondition(condition).Build();

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
