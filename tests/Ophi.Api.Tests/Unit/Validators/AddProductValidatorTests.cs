using FluentValidation.TestHelper;
using Ophi.Api.Features.Products;

namespace Ophi.Api.Tests.Unit.Validators;

public class AddProductValidatorTests
{
    private readonly AddProduct.Validator _validator = new();

    [Fact]
    public void Validate_WithValidHttpsUrl_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new AddProduct.Command("https://example.com/product");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithValidHttpUrl_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new AddProduct.Command("http://example.com/product");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyUrl_ShouldHaveError()
    {
        // Arrange
        var command = new AddProduct.Command("");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Fact]
    public void Validate_WithInvalidUrl_ShouldHaveError()
    {
        // Arrange
        var command = new AddProduct.Command("not-a-valid-url");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Url)
            .WithErrorMessage("Must be a valid HTTP or HTTPS URL");
    }

    [Fact]
    public void Validate_WithFtpUrl_ShouldHaveError()
    {
        // Arrange
        var command = new AddProduct.Command("ftp://example.com/file");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Url)
            .WithErrorMessage("Must be a valid HTTP or HTTPS URL");
    }

    [Fact]
    public void Validate_WithRelativeUrl_ShouldHaveError()
    {
        // Arrange
        var command = new AddProduct.Command("/product/123");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Fact]
    public void Validate_WithComplexUrl_ShouldNotHaveErrors()
    {
        // Arrange
        var command = new AddProduct.Command("https://www.amazon.com/dp/B08N5WRWNW?ref=cm_sw_r_cp_ud_dp_1234");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("http://localhost/admin")]
    [InlineData("http://localhost:8080/secret")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.0.0.1:5000/health")]
    [InlineData("http://10.0.0.1/internal")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://172.31.255.255")]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("https://evil.local")]
    [InlineData("https://metadata.internal")]
    // IPv6 literals — previously every one of these passed, because the guard bailed out on any
    // address whose byte length wasn't 4.
    [InlineData("http://[::1]/")]
    [InlineData("http://[fd00::1]/")]                     // unique-local (fc00::/7)
    [InlineData("http://[fc00::1]/")]
    [InlineData("http://[fe80::1]/")]                     // link-local
    [InlineData("http://[::]/")]                          // unspecified
    // IPv4-mapped IPv6 reaches the same host as the dotted form, so it must be blocked identically.
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[::ffff:169.254.169.254]/latest/meta-data")]
    [InlineData("http://[::ffff:10.0.0.1]/")]
    // Deprecated IPv4-COMPATIBLE form (RFC 4291 §2.5.5.1, no ffff block). Same embedded address,
    // and IsIPv4MappedToIPv6 does not recognize it.
    [InlineData("http://[::169.254.169.254]/latest/meta-data")]
    [InlineData("http://[::10.0.0.1]/")]
    // 0.0.0.0/8 — routes to localhost on Linux.
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://0.0.0.0:8080/admin")]
    // Other reserved ranges.
    [InlineData("http://100.64.0.1/")]                    // CGNAT
    [InlineData("http://192.0.0.1/")]                     // IETF protocol assignments
    [InlineData("http://198.18.0.1/")]                    // benchmarking
    public void Validate_WithPrivateOrReservedUrl_ShouldHaveError(string url)
    {
        var command = new AddProduct.Command(url);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://8.8.8.8/page")]
    [InlineData("http://203.0.113.1/product")]
    // Public IPv6 must still be allowed — the fix must not blanket-block v6.
    [InlineData("http://[2606:4700:4700::1111]/")]
    [InlineData("http://[2001:4860:4860::8888]/page")]
    public void Validate_WithPublicUrl_ShouldNotHaveErrors(string url)
    {
        var command = new AddProduct.Command(url);
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
