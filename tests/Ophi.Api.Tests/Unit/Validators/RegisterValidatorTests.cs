using FluentValidation.TestHelper;
using Ophi.Api.Features.Auth;

namespace Ophi.Api.Tests.Unit.Validators;

public class RegisterValidatorTests
{
    private readonly Register.Validator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new Register.Command("test@example.com", "Password123", "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    #region Email Validation

    [Fact]
    public void Validate_WithEmptyEmail_ShouldHaveError()
    {
        // Arrange
        var command = new Register.Command("", "Password123", "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Validate_WithInvalidEmail_ShouldHaveError()
    {
        // Arrange
        var command = new Register.Command("notanemail", "Password123", "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Validate_WithEmailTooLong_ShouldHaveError()
    {
        // Arrange
        var longEmail = new string('a', 250) + "@example.com";
        var command = new Register.Command(longEmail, "Password123", "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Validate_WithValidEmail_ShouldNotHaveError()
    {
        // Arrange
        var command = new Register.Command("valid@example.com", "Password123", "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    #endregion

    #region Password Validation

    [Fact]
    public void Validate_WithEmptyPassword_ShouldHaveError()
    {
        // Arrange
        var command = new Register.Command("test@example.com", "", "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Validate_WithPasswordTooShort_ShouldHaveError()
    {
        // Arrange
        var command = new Register.Command("test@example.com", "Short1", "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Validate_WithPasswordTooLong_ShouldHaveError()
    {
        // Arrange
        var longPassword = "A1" + new string('a', 130);
        var command = new Register.Command("test@example.com", longPassword, "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Validate_WithPasswordMissingUppercase_ShouldHaveError()
    {
        // Arrange
        var command = new Register.Command("test@example.com", "password123", "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must contain at least one uppercase letter");
    }

    [Fact]
    public void Validate_WithPasswordMissingLowercase_ShouldHaveError()
    {
        // Arrange
        var command = new Register.Command("test@example.com", "PASSWORD123", "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must contain at least one lowercase letter");
    }

    [Fact]
    public void Validate_WithPasswordMissingDigit_ShouldHaveError()
    {
        // Arrange
        var command = new Register.Command("test@example.com", "Passworddd", "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must contain at least one digit");
    }

    [Fact]
    public void Validate_WithValidPassword_ShouldNotHaveError()
    {
        // Arrange
        var command = new Register.Command("test@example.com", "ValidPass1", "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }

    #endregion

    #region Name Validation

    [Fact]
    public void Validate_WithEmptyName_ShouldHaveError()
    {
        // Arrange
        var command = new Register.Command("test@example.com", "Password123", "");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WithNameTooLong_ShouldHaveError()
    {
        // Arrange
        var longName = new string('a', 101);
        var command = new Register.Command("test@example.com", "Password123", longName);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WithValidName_ShouldNotHaveError()
    {
        // Arrange
        var command = new Register.Command("test@example.com", "Password123", "John Doe");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    #endregion
}
