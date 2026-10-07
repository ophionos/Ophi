using FluentValidation.TestHelper;
using Ophi.Api.Features.Products;

namespace Ophi.Api.Tests.Unit.Products;

public class UpdateProductValidatorTests
{
    private readonly UpdateProduct.Validator _validator = new();

    [Theory]
    [InlineData(15)]
    [InlineData(60)]
    [InlineData(1440)]
    public void Validate_WithValidCheckInterval_Passes(int interval)
    {
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, null, null, null, null, interval)
        {
            UserId = Guid.NewGuid()
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.CheckIntervalMinutes);
    }

    [Fact]
    public void Validate_WithCheckIntervalZero_Passes()
    {
        // 0 = reset to default, valid
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, null, null, null, null, 0)
        {
            UserId = Guid.NewGuid()
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.CheckIntervalMinutes);
    }

    [Fact]
    public void Validate_WithCheckIntervalBelowMin_Fails()
    {
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, null, null, null, null, 14)
        {
            UserId = Guid.NewGuid()
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.CheckIntervalMinutes);
    }

    [Fact]
    public void Validate_WithCheckIntervalAboveMax_Fails()
    {
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, null, null, null, null, 1441)
        {
            UserId = Guid.NewGuid()
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.CheckIntervalMinutes);
    }

    [Fact]
    public void Validate_WithNullCheckInterval_Passes()
    {
        var command = new UpdateProduct.Command(Guid.NewGuid(), "Name", null, null, null)
        {
            UserId = Guid.NewGuid()
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.CheckIntervalMinutes);
    }

    [Fact]
    public void Validate_WithOnlyCheckInterval_Passes()
    {
        // CheckIntervalMinutes alone satisfies "at least one field"
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, null, null, null, null, 60)
        {
            UserId = Guid.NewGuid()
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
