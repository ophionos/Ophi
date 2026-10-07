using FluentValidation.TestHelper;
using Ophi.Api.Features.Comparisons;

namespace Ophi.Api.Tests.Unit.Comparisons;

public class CreateComparisonGroupValidatorTests
{
    private readonly CreateComparisonGroup.Validator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new CreateComparisonGroup.Command("Laptops", "Compare laptop prices")
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyName_ShouldHaveError()
    {
        // Arrange
        var command = new CreateComparisonGroup.Command("", "Some description")
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WithNameOver100Chars_ShouldHaveError()
    {
        // Arrange
        var longName = new string('a', 101);
        var command = new CreateComparisonGroup.Command(longName, null)
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WithDescriptionOver500Chars_ShouldHaveError()
    {
        // Arrange
        var longDescription = new string('a', 501);
        var command = new CreateComparisonGroup.Command("Laptops", longDescription)
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Validate_WithNullDescription_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new CreateComparisonGroup.Command("Laptops", null)
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}
