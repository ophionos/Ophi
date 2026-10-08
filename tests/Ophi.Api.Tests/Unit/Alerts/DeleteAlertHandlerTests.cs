using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Alerts;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Alerts;

public class DeleteAlertHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly DeleteAlert.Handler _handler;
    private readonly Guid _testUserId;

    public DeleteAlertHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new DeleteAlert.Handler(_dbContext, NullLogger<DeleteAlert.Handler>.Instance);
        _testUserId = Guid.NewGuid();

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
    public async Task Handle_WithValidAlert_DeletesAlert()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        var alert = CreateAlert(product.Id, 50m);
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteAlert.Command(alert.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var deletedAlert = await _dbContext.Alerts.FindAsync([alert.Id], TestContext.Current.CancellationToken);
        deletedAlert.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithValidAlert_RemovesAlertFromDatabase()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        var alert = CreateAlert(product.Id, 50m);
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var initialCount = await _dbContext.Alerts.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        var command = new DeleteAlert.Command(alert.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var finalCount = await _dbContext.Alerts.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        finalCount.Should().Be(initialCount - 1);
    }

    [Fact]
    public async Task Handle_WithNonExistentAlert_ThrowsNotFoundException()
    {
        // Arrange
        var nonExistentAlertId = Guid.NewGuid();
        var command = new DeleteAlert.Command(nonExistentAlertId, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Alert not found");
    }

    [Fact]
    public async Task Handle_WithAlertBelongingToDifferentUser_ThrowsNotFoundException()
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

        var otherProduct = new Product
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(otherProduct);

        var otherUsersAlert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = otherProduct.Id,
            UserId = otherUserId,
            TargetPrice = 50m,
            Condition = AlertCondition.Below,
            IsActive = true
        };
        _dbContext.Alerts.Add(otherUsersAlert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteAlert.Command(otherUsersAlert.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Alert not found");
    }

    [Fact]
    public async Task Handle_WithAlertBelongingToDifferentUser_DoesNotDeleteAlert()
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

        var otherProduct = new Product
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(otherProduct);

        var otherUsersAlert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = otherProduct.Id,
            UserId = otherUserId,
            TargetPrice = 50m,
            Condition = AlertCondition.Below,
            IsActive = true
        };
        _dbContext.Alerts.Add(otherUsersAlert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteAlert.Command(otherUsersAlert.Id, _testUserId);

        // Act
        try
        {
            await _handler.Handle(command, TestContext.Current.CancellationToken);
        }
        catch (NotFoundException)
        {
            // Expected
        }

        // Assert - alert should still exist
        var alert = await _dbContext.Alerts.FindAsync([otherUsersAlert.Id], TestContext.Current.CancellationToken);
        alert.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_DoesNotAffectOtherAlerts()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        var alertToDelete = CreateAlert(product.Id, 50m);
        var alertToKeep = CreateAlert(product.Id, 75m);

        _dbContext.Alerts.AddRange(alertToDelete, alertToKeep);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteAlert.Command(alertToDelete.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var remainingAlerts = await _dbContext.Alerts.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        remainingAlerts.Should().HaveCount(1);
        remainingAlerts[0].Id.Should().Be(alertToKeep.Id);
    }

    [Fact]
    public async Task Handle_WithInactiveAlert_DeletesSuccessfully()
    {
        // Arrange
        var product = CreateProduct("Test Product");
        _dbContext.Products.Add(product);

        var alert = CreateAlert(product.Id, 50m);
        alert.Pause();
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteAlert.Command(alert.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var deletedAlert = await _dbContext.Alerts.FindAsync([alert.Id], TestContext.Current.CancellationToken);
        deletedAlert.Should().BeNull();
    }

    private Product CreateProduct(string name) =>
        TestEntityFactory.Product(_testUserId).Named(name).Build();

    private Alert CreateAlert(Guid productId, decimal targetPrice) =>
        TestEntityFactory.Alert(productId, _testUserId).WithTarget(targetPrice).Build();

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
