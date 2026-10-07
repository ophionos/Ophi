using FluentAssertions;
using FluentValidation.TestHelper;
using Ophi.Api.Features.Products;

namespace Ophi.Api.Tests.Unit.Validators;

public class UpdateProductValidatorTests
{
    private readonly UpdateProduct.Validator _validator = new();

    [Fact]
    public void Validate_WithValidName_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new UpdateProduct.Command(Guid.NewGuid(), "Valid Name", null, null, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyName_ShouldHaveError()
    {
        // Arrange
        var command = new UpdateProduct.Command(Guid.NewGuid(), "", null, null, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Name cannot be empty");
    }

    [Fact]
    public void Validate_WithNameTooLong_ShouldHaveError()
    {
        // Arrange
        var longName = new string('a', 501);
        var command = new UpdateProduct.Command(Guid.NewGuid(), longName, null, null, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Name must not exceed 500 characters");
    }

    [Fact]
    public void Validate_WithValidImageUrl_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, "https://example.com/image.jpg", null, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithImageUrlTooLong_ShouldHaveError()
    {
        // Arrange
        var longUrl = "https://example.com/" + new string('a', 2030);
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, longUrl, null, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ImageUrl)
            .WithErrorMessage("Image URL must not exceed 2048 characters");
    }

    [Fact]
    public void Validate_WithInvalidImageUrl_ShouldHaveError()
    {
        // Arrange
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, "not-a-valid-url", null, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ImageUrl)
            .WithErrorMessage("Must be a valid HTTP or HTTPS URL");
    }

    [Theory]
    [InlineData("active")]
    [InlineData("paused")]
    [InlineData("Active")]
    [InlineData("Paused")]
    public void Validate_WithValidStatus_ShouldNotHaveErrors(string status)
    {
        // Arrange
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, null, status, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("error")]
    [InlineData("notfound")]
    [InlineData("invalid")]
    public void Validate_WithInvalidStatus_ShouldHaveError(string status)
    {
        // Arrange
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, null, status, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Status)
            .WithErrorMessage("Status must be 'active' or 'paused'");
    }

    [Fact]
    public void Validate_WithAllFieldsNull_ShouldHaveError()
    {
        // Arrange
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, null, null, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "At least one field must be provided");
    }

    [Fact]
    public void Validate_WithOnlyIsFavourite_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, null, null, true);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithValidCustomFields_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new UpdateProduct.Command(
            Guid.NewGuid(), null, null, null, null,
            [new CustomFieldDto("Color", "Red"), new CustomFieldDto("Size", "Large")]);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyCustomFieldName_ShouldHaveError()
    {
        // Arrange
        var command = new UpdateProduct.Command(
            Guid.NewGuid(), null, null, null, null,
            [new CustomFieldDto("", "Red")]);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Custom field name is required");
    }

    [Fact]
    public void Validate_WithCustomFieldNameTooLong_ShouldHaveError()
    {
        // Arrange
        var command = new UpdateProduct.Command(
            Guid.NewGuid(), null, null, null, null,
            [new CustomFieldDto(new string('a', 101), "Red")]);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Custom field name must not exceed 100 characters");
    }

    [Fact]
    public void Validate_WithCustomFieldValueTooLong_ShouldHaveError()
    {
        // Arrange
        var command = new UpdateProduct.Command(
            Guid.NewGuid(), null, null, null, null,
            [new CustomFieldDto("Color", new string('a', 1001))]);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Custom field value must not exceed 1000 characters");
    }

    [Fact]
    public void Validate_WithTooManyCustomFields_ShouldHaveError()
    {
        // Arrange
        var fields = Enumerable.Range(1, 51)
            .Select(i => new CustomFieldDto($"Field{i}", $"Value{i}"))
            .ToList();
        var command = new UpdateProduct.Command(Guid.NewGuid(), null, null, null, null, fields);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Cannot have more than 50 custom fields");
    }

    [Fact]
    public void Validate_WithDuplicateCustomFieldNames_ShouldHaveError()
    {
        // Arrange
        var command = new UpdateProduct.Command(
            Guid.NewGuid(), null, null, null, null,
            [new CustomFieldDto("Color", "Red"), new CustomFieldDto("color", "Blue")]);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Custom field names must be unique");
    }

    [Fact]
    public void Validate_WithOnlyCustomFields_ShouldNotHaveErrors()
    {
        // Arrange - only custom fields provided, all other fields null
        var command = new UpdateProduct.Command(
            Guid.NewGuid(), null, null, null, null,
            [new CustomFieldDto("Color", "Red")]);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}
