using FluentValidation.TestHelper;
using Ophi.Api.Features.Account;

namespace Ophi.Api.Tests.Unit.Validators;

public class UpdateProfileValidatorTests
{
    private readonly UpdateProfile.Validator _validator = new();

    [Fact]
    public void Validate_WithValidNameAndEmail_Passes()
    {
        var command = new UpdateProfile.Command("Valid Name", "valid@example.com", null);

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_WithMissingName_Fails(string? name)
    {
        var command = new UpdateProfile.Command(name!, "valid@example.com", null);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WithOverlongName_Fails()
    {
        var command = new UpdateProfile.Command(new string('x', 101), "valid@example.com", null);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Validate_WithInvalidEmail_Fails(string email)
    {
        var command = new UpdateProfile.Command("Valid Name", email, null);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }
}
