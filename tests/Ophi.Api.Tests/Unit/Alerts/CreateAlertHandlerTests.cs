using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Alerts;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;

namespace Ophi.Api.Tests.Unit.Alerts;

public class CreateAlertHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly CreateAlert.Handler _handler;
    private readonly Guid _testUserId;
    private readonly Guid _testProductId;

    public CreateAlertHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        var alertSettings = Options.Create(new AlertSettings { MaxAlertsPerUser = 100, CooldownMinutes = 60 });
        _handler = new CreateAlert.Handler(_dbContext, alertSettings, NullLogger<CreateAlert.Handler>.Instance);
        _testUserId = Guid.NewGuid();
        _testProductId = Guid.NewGuid();

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
    public async Task Handle_WithValidData_CreatesAlert()
    {
        // Arrange
        var product = CreateProduct();
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateAlert.Command(_testProductId, 99.99m, "below") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.ProductId.Should().Be(_testProductId);
        result.TargetPrice.Should().Be(99.99m);
        result.Condition.Should().Be("below");
        result.Active.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithValidData_SavesAlertToDatabase()
    {
        // Arrange
        var product = CreateProduct();
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateAlert.Command(_testProductId, 99.99m, "below") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var savedAlert = await _dbContext.Alerts.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedAlert.Should().NotBeNull();
        savedAlert.UserId.Should().Be(_testUserId);
        savedAlert.ProductId.Should().Be(_testProductId);
        savedAlert.TargetPrice.Should().Be(99.99m);
        savedAlert.Condition.Should().Be(AlertCondition.Below);
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsNotFoundException()
    {
        // Arrange
        var command = new CreateAlert.Command(Guid.NewGuid(), 99.99m, "below") { UserId = _testUserId }; // Product doesn't exist

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product not found");
    }

    [Fact]
    public async Task Handle_WithProductBelongingToDifferentUser_ThrowsNotFoundException()
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

        var product = CreateProduct();
        product.UserId = otherUserId; // Different user
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateAlert.Command(_testProductId, 99.99m, "below") { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product not found");
    }

    [Theory]
    [InlineData("below", AlertCondition.Below)]
    [InlineData("above", AlertCondition.Above)]
    [InlineData("percentDrop", AlertCondition.PercentDrop)]
    public async Task Handle_WithDifferentConditions_MapsCorrectly(string conditionString, AlertCondition expectedCondition)
    {
        // Arrange
        var product = CreateProduct();
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateAlert.Command(_testProductId, 99.99m, conditionString) { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var savedAlert = await _dbContext.Alerts.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedAlert!.Condition.Should().Be(expectedCondition);
    }

    // Note: validation of Condition and TargetPrice is handled by Wolverine middleware
    // (UseFluentValidation) before the handler runs. See CreateAlertValidatorTests for coverage.

    [Fact]
    public async Task Handle_WithUserExceedingLimit_ThrowsException()
    {
        // Arrange
        // Use a fresh user to ensure no conflicts
        var limitTestUserId = Guid.NewGuid();
        var limitTestUser = new User
        {
            Id = limitTestUserId,
            Email = "limit@example.com",
            Name = "Limit User",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(limitTestUser);
        
        // Product for this user
        var limitTestProduct = new Product
        {
            Id = Guid.NewGuid(),
            UserId = limitTestUserId, // Link to fresh user
            Name = "Limit Product",
            User = limitTestUser // Navigation property
        };
        _dbContext.Products.Add(limitTestProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Seed 100 active alerts for this user
        var alerts = new List<Alert>();
        for (var i = 0; i < 100; i++)
        {
            alerts.Add(new Alert
            {
                Id = Guid.NewGuid(),
                ProductId = limitTestProduct.Id,
                UserId = limitTestUserId,
                TargetPrice = 100,
                Condition = AlertCondition.Below,
                IsActive = true,
                Product = limitTestProduct,
                User = limitTestUser
            });
        }
        _dbContext.Alerts.AddRange(alerts);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // New command for this user
        // We need another product for the NEW command (the 101st alert), logic checks count for USER
        // So we can use the same product or a new one. Let's use a new one to mimic "adding another alert".
        var newProduct = new Product
        {
             Id = Guid.NewGuid(),
             UserId = limitTestUserId,
             Name = "New Product",
             User = limitTestUser
        };
        _dbContext.Products.Add(newProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        
        var command = new CreateAlert.Command(newProduct.Id, 99.99m, "below") { UserId = limitTestUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .Where(e => e.StatusCode == 422 && e.ErrorCode == "MaxAlertsReached")
            .WithMessage("*Maximum number of active alerts*");
    }

    [Fact]
    public async Task Handle_WithPercentDrop_SetsReferencePrice()
    {
        // Arrange
        var product = CreateProduct();
        product.CurrentPrice = 200.00m; // Reference price
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateAlert.Command(_testProductId, 150.00m, "percentDrop") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var savedAlert = await _dbContext.Alerts.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedAlert!.ReferencePrice.Should().Be(200.00m);
    }

    [Fact]
    public async Task Handle_WithValidData_SetsIsActiveToTrue()
    {
        // Arrange
        var product = CreateProduct();
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateAlert.Command(_testProductId, 99.99m, "below") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var savedAlert = await _dbContext.Alerts.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedAlert!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithPercentDrop_WhenCurrentPriceIsNull_UsesTargetPriceAsReference()
    {
        // Arrange
        var product = CreateProduct();
        product.CurrentPrice = null;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateAlert.Command(_testProductId, 50.00m, "percentDrop") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var savedAlert = await _dbContext.Alerts.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedAlert!.ReferencePrice.Should().Be(50.00m);
    }

    [Fact]
    public async Task Handle_WithBelowCondition_SetsReferencePriceFromCurrentPrice()
    {
        // Arrange
        var product = CreateProduct();
        product.CurrentPrice = 150.00m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateAlert.Command(_testProductId, 99.99m, "below") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var savedAlert = await _dbContext.Alerts.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedAlert!.ReferencePrice.Should().Be(150.00m);
    }

    [Fact]
    public async Task Handle_StampsTheProductsCurrencyOntoTheAlert()
    {
        // The target means "80 of whatever the user was looking at". Capturing that denomination at
        // creation is what lets a later re-anchor be detected instead of silently reinterpreting the
        // number — an alert created against a GBP product must not become a USD target.
        var product = TestEntityFactory.Product(_testUserId)
            .WithId(_testProductId).Priced(120.00m).WithCurrency("GBP").Build();
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateAlert.Command(_testProductId, 99.99m, "below") { UserId = _testUserId };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        var savedAlert = await _dbContext.Alerts.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedAlert!.Currency.Should().Be("GBP");
        savedAlert.HasCurrencyMismatch(product.Currency).Should().BeFalse(
            "an alert must be live against the product it was just created for");
    }

    [Fact]
    public async Task Handle_ReturnsTheSameAlertShapeAsGetAlerts()
    {
        // The web client has one Alert type covering both endpoints, and it declares ProductName,
        // Currency, ProductCurrency and HasCurrencyMismatch as required. Returning less here makes
        // the declaration a lie that neither the compiler nor svelte-check can catch — the mismatch
        // is in the declaration, not in any usage. The handler already loads the product, so every
        // one of these is free.
        var product = TestEntityFactory.Product(_testUserId)
            .WithId(_testProductId).Named("Sennheiser HD 600").Priced(120.00m).WithCurrency("GBP").Build();
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateAlert.Command(_testProductId, 99.99m, "below") { UserId = _testUserId };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.ProductName.Should().Be("Sennheiser HD 600");
        result.CurrentPrice.Should().Be(120.00m);
        result.Currency.Should().Be("GBP");
        result.ProductCurrency.Should().Be("GBP");
        result.HasCurrencyMismatch.Should().BeFalse(
            "a just-created alert is stamped with the product's own currency");
        result.LastTriggered.Should().BeNull("a just-created alert has never fired");
    }

    [Fact]
    public async Task Handle_WithAnUnpricedProduct_ReturnsNoCurrentPrice()
    {
        // CurrentPrice is the one nullable field in the contract; a product that has never scraped
        // successfully must come back as null rather than as the target price standing in for it.
        var product = TestEntityFactory.Product(_testUserId)
            .WithId(_testProductId).Priced(null).Build();
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateAlert.Command(_testProductId, 99.99m, "below") { UserId = _testUserId };

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.CurrentPrice.Should().BeNull();
    }

    private Product CreateProduct() =>
        TestEntityFactory.Product(_testUserId).WithId(_testProductId).Priced(120.00m).Build();

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
