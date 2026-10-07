using FluentValidation.TestHelper;
using Ophi.Api.Features.Auth;

namespace Ophi.Api.Tests.Unit.Validators;

public class ResetPasswordValidatorTests
{
    private readonly ResetPassword.Validator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_ShouldNotHaveErrors()
    {
        var command = new ResetPassword.Command("sometoken", "ValidPass1");

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyToken_ShouldHaveError()
    {
        var command = new ResetPassword.Command("", "ValidPass1");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Token);
    }

    [Fact]
    public void Validate_WithEmptyPassword_ShouldHaveError()
    {
        var command = new ResetPassword.Command("sometoken", "");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void Validate_WithPasswordTooShort_ShouldHaveError()
    {
        var command = new ResetPassword.Command("sometoken", "Short1");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void Validate_WithPasswordTooLong_ShouldHaveError()
    {
        var longPassword = "A1" + new string('a', 130);
        var command = new ResetPassword.Command("sometoken", longPassword);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void Validate_WithPasswordMissingUppercase_ShouldHaveError()
    {
        var command = new ResetPassword.Command("sometoken", "password123");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NewPassword)
            .WithErrorMessage("Password must contain at least one uppercase letter");
    }

    [Fact]
    public void Validate_WithPasswordMissingLowercase_ShouldHaveError()
    {
        var command = new ResetPassword.Command("sometoken", "PASSWORD123");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NewPassword)
            .WithErrorMessage("Password must contain at least one lowercase letter");
    }

    [Fact]
    public void Validate_WithPasswordMissingDigit_ShouldHaveError()
    {
        var command = new ResetPassword.Command("sometoken", "Passworddd");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NewPassword)
            .WithErrorMessage("Password must contain at least one digit");
    }
}
