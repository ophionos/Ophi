using FluentValidation.TestHelper;
using Ophi.Api.Features.Tags;

namespace Ophi.Api.Tests.Unit.Validators;

public class UpdateTagValidatorTests
{
    private readonly UpdateTag.Validator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new UpdateTag.Command(Guid.NewGuid(), "Electronics", "#3B82F6", 10);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithAllNullValues_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new UpdateTag.Command(Guid.NewGuid(), null, null, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithNameTooLong_ShouldHaveError()
    {
        // Arrange
        var longName = new string('a', 51);
        var command = new UpdateTag.Command(Guid.NewGuid(), longName, null, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WithNameAtMaxLength_ShouldNotHaveError()
    {
        // Arrange
        var maxLengthName = new string('a', 50);
        var command = new UpdateTag.Command(Guid.NewGuid(), maxLengthName, null, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WithValidColor_ShouldNotHaveError()
    {
        // Arrange
        var command = new UpdateTag.Command(Guid.NewGuid(), null, "#FF0000", null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Color);
    }

    [Fact]
    public void Validate_WithInvalidColor_ShouldHaveError()
    {
        // Arrange
        var command = new UpdateTag.Command(Guid.NewGuid(), null, "invalid", null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Color);
    }

    [Fact]
    public void Validate_WithColorMissingHash_ShouldHaveError()
    {
        // Arrange
        var command = new UpdateTag.Command(Guid.NewGuid(), null, "FF0000", null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Color);
    }

    [Fact]
    public void Validate_WithColorTooShort_ShouldHaveError()
    {
        // Arrange
        var command = new UpdateTag.Command(Guid.NewGuid(), null, "#FFF", null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Color);
    }

    [Fact]
    public void Validate_WithLowercaseColor_ShouldNotHaveError()
    {
        // Arrange
        var command = new UpdateTag.Command(Guid.NewGuid(), null, "#ff0000", null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Color);
    }
}
