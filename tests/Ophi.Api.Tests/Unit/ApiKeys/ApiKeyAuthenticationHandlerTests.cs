using FluentAssertions;
using Ophi.Api.Common.Auth;

namespace Ophi.Api.Tests.Unit.ApiKeys;

public class ApiKeyAuthenticationHandlerTests
{
    [Fact]
    public void HashKey_ProducesDeterministicOutput()
    {
        var hash1 = ApiKeyAuthenticationHandler.HashKey("test-key-123");
        var hash2 = ApiKeyAuthenticationHandler.HashKey("test-key-123");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void HashKey_ProducesDifferentOutputForDifferentKeys()
    {
        var hash1 = ApiKeyAuthenticationHandler.HashKey("key-one");
        var hash2 = ApiKeyAuthenticationHandler.HashKey("key-two");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void HashKey_ReturnsLowercaseHex64Chars()
    {
        var hash = ApiKeyAuthenticationHandler.HashKey("any-key");

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
