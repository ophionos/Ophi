using FluentValidation.TestHelper;
using Ophi.Api.Features.Alerts;

namespace Ophi.Api.Tests.Unit.Validators;

public class CreateAlertValidatorTests
{
    private readonly CreateAlert.Validator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new CreateAlert.Command(Guid.NewGuid(), 50m, "below");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    #region ProductId Validation

    [Fact]
    public void Validate_WithEmptyProductId_ShouldHaveError()
    {
        // Arrange
        var command = new CreateAlert.Command(Guid.Empty, 50m, "below");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ProductId);
    }

    [Fact]
    public void Validate_WithValidProductId_ShouldNotHaveError()
    {
        // Arrange
        var command = new CreateAlert.Command(Guid.NewGuid(), 50m, "below");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.ProductId);
    }

    #endregion

    #region TargetPrice Validation

    [Fact]
    public void Validate_WithZeroTargetPrice_ShouldHaveError()
    {
        // Arrange
        var command = new CreateAlert.Command(Guid.NewGuid(), 0m, "below");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TargetPrice);
    }

    [Fact]
    public void Validate_WithNegativeTargetPrice_ShouldHaveError()
    {
        // Arrange
        var command = new CreateAlert.Command(Guid.NewGuid(), -10m, "below");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TargetPrice);
    }

    [Fact]
    public void Validate_WithPositiveTargetPrice_ShouldNotHaveError()
    {
        // Arrange
        var command = new CreateAlert.Command(Guid.NewGuid(), 99.99m, "below");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.TargetPrice);
    }

    #endregion

    #region Condition Validation

    [Fact]
    public void Validate_WithEmptyCondition_ShouldHaveError()
    {
        // Arrange
        var command = new CreateAlert.Command(Guid.NewGuid(), 50m, "");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Condition);
    }

    [Theory]
    [InlineData("below")]
    [InlineData("above")]
    [InlineData("percentDrop")]
    public void Validate_WithValidCondition_ShouldNotHaveError(string condition)
    {
        // Arrange
        var command = new CreateAlert.Command(Guid.NewGuid(), 50m, condition);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Condition);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("Below")]
    [InlineData("ABOVE")]
    [InlineData("percent_drop")]
    public void Validate_WithInvalidCondition_ShouldHaveError(string condition)
    {
        // Arrange
        var command = new CreateAlert.Command(Guid.NewGuid(), 50m, condition);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Condition)
            .WithErrorMessage("Condition must be 'below', 'above', or 'percentDrop'");
    }

    #endregion

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void Validate_WithPercentDropOfAtLeast100_ShouldHaveError(decimal percent)
    {
        // A price is > 0, so a drop of 100 % or more can never happen and the alert would never fire.
        var result = _validator.TestValidate(new CreateAlert.Command(Guid.NewGuid(), percent, "percentDrop"));

        result.ShouldHaveValidationErrorFor(x => x.TargetPrice);
    }

    [Fact]
    public void Validate_WithBelowTargetOver100_ShouldNotHaveError()
    {
        var result = _validator.TestValidate(new CreateAlert.Command(Guid.NewGuid(), 250m, "below"));

        result.ShouldNotHaveValidationErrorFor(x => x.TargetPrice);
    }
}
