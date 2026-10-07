using FluentValidation.TestHelper;
using Ophi.Api.Features.Auth;

namespace Ophi.Api.Tests.Unit.Validators;

public class ForgotPasswordValidatorTests
{
    private readonly ForgotPassword.Validator _validator = new();

    [Fact]
    public void Validate_WithValidEmail_ShouldNotHaveErrors()
    {
        var command = new ForgotPassword.Command("user@example.com");

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyEmail_ShouldHaveError()
    {
        var command = new ForgotPassword.Command("");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Validate_WithInvalidEmail_ShouldHaveError()
    {
        var command = new ForgotPassword.Command("notanemail");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }
}
