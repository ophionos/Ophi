using FluentValidation.TestHelper;
using Ophi.Api.Features.Stores;

namespace Ophi.Api.Tests.Unit.Stores;

public class SetStoreAffiliateValidatorTests
{
    private readonly SetStoreAffiliate.Validator _validator = new();

    private static SetStoreAffiliate.Command ValidCommand() =>
        new("amazon", "tag", "my-affiliate-20")
        { UserId = Guid.NewGuid() };

    [Fact]
    public void Validate_WithValidCommand_ShouldNotHaveErrors()
    {
        // Arrange
        var command = ValidCommand();

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyStoreId_ShouldHaveError()
    {
        // Arrange
        var command = new SetStoreAffiliate.Command("", "tag", "my-affiliate-20")
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.StoreId);
    }

    [Fact]
    public void Validate_WithNullParamName_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new SetStoreAffiliate.Command("amazon", null, null)
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyParamName_ShouldNotHaveErrors()
    {
        // Arrange — empty string is treated like null (clearing affiliate)
        // because the When guard uses string.IsNullOrEmpty
        var command = new SetStoreAffiliate.Command("amazon", "", null)
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithWhitespaceParamName_ShouldHaveError()
    {
        // Arrange
        var command = new SetStoreAffiliate.Command("amazon", "param name", "my-affiliate-20")
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.AffiliateParamName);
    }

    [Fact]
    public void Validate_WithParamNameOver50Chars_ShouldHaveError()
    {
        // Arrange
        var longParamName = new string('a', 51);
        var command = new SetStoreAffiliate.Command("amazon", longParamName, "my-affiliate-20")
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.AffiliateParamName);
    }

    [Fact]
    public void Validate_WithTagOver100Chars_ShouldHaveError()
    {
        // Arrange
        var longTag = new string('a', 101);
        var command = new SetStoreAffiliate.Command("amazon", "tag", longTag)
        { UserId = Guid.NewGuid() };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.AffiliateTag);
    }
}
