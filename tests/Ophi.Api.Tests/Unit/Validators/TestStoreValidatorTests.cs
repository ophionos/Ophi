using FluentValidation.TestHelper;
using Ophi.Api.Features.Stores;

namespace Ophi.Api.Tests.Unit.Validators;

public class TestStoreValidatorTests
{
    private readonly TestStore.Validator _validator = new();

    [Fact]
    public void Validate_WithValidData_ShouldNotHaveErrors()
    {
        var command = new TestStore.Command("my-store", "https://example.com/product/1");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyStoreId_ShouldHaveError()
    {
        var command = new TestStore.Command("", "https://example.com/product/1");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.StoreId);
    }

    [Fact]
    public void Validate_WithEmptyUrl_ShouldHaveError()
    {
        var command = new TestStore.Command("my-store", "");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.TestUrl);
    }

    [Fact]
    public void Validate_WithInvalidUrl_ShouldHaveError()
    {
        var command = new TestStore.Command("my-store", "not-a-url");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.TestUrl);
    }

    [Fact]
    public void Validate_WithHttpUrl_ShouldNotHaveErrors()
    {
        var command = new TestStore.Command("my-store", "http://example.com/product");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithFtpUrl_ShouldHaveError()
    {
        var command = new TestStore.Command("my-store", "ftp://example.com/file");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.TestUrl);
    }
}
