using FluentValidation.TestHelper;
using Ophi.Api.Features.Products;

namespace Ophi.Api.Tests.Unit.Validators;

public class AddProductUrlValidatorTests
{
    private readonly AddProductUrl.Validator _validator = new();

    [Fact]
    public void Validate_WithValidUrl_ShouldPass()
    {
        var command = new AddProductUrl.Command(Guid.NewGuid(), "https://example.com/product") { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyUrl_ShouldFail()
    {
        var command = new AddProductUrl.Command(Guid.NewGuid(), "") { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Fact]
    public void Validate_WithInvalidUrl_ShouldFail()
    {
        var command = new AddProductUrl.Command(Guid.NewGuid(), "not-a-url") { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Fact]
    public void Validate_WithFtpUrl_ShouldFail()
    {
        var command = new AddProductUrl.Command(Guid.NewGuid(), "ftp://example.com/file") { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Fact]
    public void Validate_WithHttpUrl_ShouldPass()
    {
        var command = new AddProductUrl.Command(Guid.NewGuid(), "http://example.com/product") { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyProductId_ShouldFail()
    {
        var command = new AddProductUrl.Command(Guid.Empty, "https://example.com/product") { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ProductId);
    }
}
