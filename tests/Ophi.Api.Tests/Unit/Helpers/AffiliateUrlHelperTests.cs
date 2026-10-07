using FluentAssertions;
using Ophi.Api.Common.Helpers;

namespace Ophi.Api.Tests.Unit.Helpers;

public class AffiliateUrlHelperTests
{
    [Fact]
    public void ApplyAffiliateCode_WithCleanUrl_AppendsQueryParam()
    {
        var result = AffiliateUrlHelper.ApplyAffiliateCode(
            "https://amazon.com/dp/B08N5WRWNW", "tag", "ophi-20");

        result.Should().Be("https://amazon.com/dp/B08N5WRWNW?tag=ophi-20");
    }

    [Fact]
    public void ApplyAffiliateCode_WithExistingParams_AppendsWithAmpersand()
    {
        var result = AffiliateUrlHelper.ApplyAffiliateCode(
            "https://amazon.com/dp/B08N5WRWNW?ref=sr_1_1", "tag", "ophi-20");

        result.Should().Be("https://amazon.com/dp/B08N5WRWNW?ref=sr_1_1&tag=ophi-20");
    }

    [Fact]
    public void ApplyAffiliateCode_WithExistingAffiliateParam_ReplacesValue()
    {
        var result = AffiliateUrlHelper.ApplyAffiliateCode(
            "https://amazon.com/dp/B08N5WRWNW?tag=old-code&ref=sr_1_1", "tag", "ophi-20");

        result.Should().Be("https://amazon.com/dp/B08N5WRWNW?ref=sr_1_1&tag=ophi-20");
    }

    [Theory]
    [InlineData(null, "ophi-20")]
    [InlineData("", "ophi-20")]
    [InlineData("tag", null)]
    [InlineData("tag", "")]
    [InlineData(null, null)]
    public void ApplyAffiliateCode_WithNullOrEmptyParams_ReturnsOriginalUrl(
        string? paramName, string? tag)
    {
        var url = "https://amazon.com/dp/B08N5WRWNW";
        var result = AffiliateUrlHelper.ApplyAffiliateCode(url, paramName, tag);

        result.Should().Be(url);
    }

    [Fact]
    public void ApplyAffiliateCode_WithMalformedUrl_ReturnsOriginalUrl()
    {
        var url = "not-a-valid-url";
        var result = AffiliateUrlHelper.ApplyAffiliateCode(url, "tag", "ophi-20");

        result.Should().Be(url);
    }

    [Fact]
    public void ApplyAffiliateCode_WithTrailingSlash_AppendsCorrectly()
    {
        var result = AffiliateUrlHelper.ApplyAffiliateCode(
            "https://example.com/product/", "ref", "abc123");

        result.Should().Be("https://example.com/product/?ref=abc123");
    }

    [Fact]
    public void ApplyAffiliateCode_WithFragment_PreservesFragment()
    {
        var result = AffiliateUrlHelper.ApplyAffiliateCode(
            "https://example.com/product#details", "ref", "abc123");

        result.Should().Be("https://example.com/product?ref=abc123#details");
    }

    [Fact]
    public void ApplyAffiliateCode_WithSpecialCharsInTag_EncodesCorrectly()
    {
        var result = AffiliateUrlHelper.ApplyAffiliateCode(
            "https://example.com/product", "ref", "code&value=x");

        result.Should().Contain("ref=code%26value%3d", "encoded special characters should be present");
    }

    [Fact]
    public void ApplyAffiliateCode_CaseInsensitiveParamMatch_ReplacesExisting()
    {
        var result = AffiliateUrlHelper.ApplyAffiliateCode(
            "https://amazon.com/dp/B08N5WRWNW?Tag=old-code", "tag", "ophi-20");

        result.Should().Contain("tag=ophi-20");
        result.Should().NotContain("Tag=old-code");
    }
}
