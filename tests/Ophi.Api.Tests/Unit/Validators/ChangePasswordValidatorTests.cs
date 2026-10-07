using FluentValidation.TestHelper;
using Ophi.Api.Features.Account;

namespace Ophi.Api.Tests.Unit.Validators;

public class ChangePasswordValidatorTests
{
    private readonly ChangePassword.Validator _validator = new();

    [Fact]
    public void Validate_WithValidPasswords_Passes()
    {
        var command = new ChangePassword.Command("OldPassword1", "NewPassword1");

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyCurrentPassword_Fails()
    {
        var command = new ChangePassword.Command("", "NewPassword1");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.CurrentPassword);
    }

    [Theory]
    [InlineData("")]               // empty
    [InlineData("Sh0rt")]          // too short
    [InlineData("nouppercase1")]   // no uppercase
    [InlineData("NOLOWERCASE1")]   // no lowercase
    [InlineData("NoDigitsHere")]   // no digit
    public void Validate_WithWeakNewPassword_Fails(string newPassword)
    {
        var command = new ChangePassword.Command("OldPassword1", newPassword);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }
}
