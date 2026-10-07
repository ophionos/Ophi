using FluentValidation.TestHelper;
using Ophi.Api.Features.Comparisons;

namespace Ophi.Api.Tests.Unit.Comparisons;

public class AddProductToGroupValidatorTests
{
    private readonly AddProductToGroup.Validator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new AddProductToGroup.Command(Guid.NewGuid(), Guid.NewGuid())
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyGroupId_ShouldHaveError()
    {
        // Arrange
        var command = new AddProductToGroup.Command(Guid.Empty, Guid.NewGuid())
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.GroupId);
    }

    [Fact]
    public void Validate_WithEmptyProductId_ShouldHaveError()
    {
        // Arrange
        var command = new AddProductToGroup.Command(Guid.NewGuid(), Guid.Empty)
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ProductId);
    }
}
