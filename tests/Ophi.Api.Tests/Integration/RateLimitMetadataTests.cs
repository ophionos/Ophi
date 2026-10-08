using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Api.Common;

namespace Ophi.Api.Tests.Integration;

/// <summary>
/// The Testing environment skips the limiters, so this checks the endpoint metadata instead: every
/// endpoint that makes the server call a caller-chosen URL carries the per-user outbound-fetch policy.
/// </summary>
public class RateLimitMetadataTests(OphiWebApplicationFactory factory) : IClassFixture<OphiWebApplicationFactory>
{
    [Theory]
    [InlineData("DetectStore")]
    [InlineData("TestStore")]
    [InlineData("TestWebhookTarget")]
    [InlineData("TestDiscordWebhook")]
    [InlineData("TestPushChannel")]
    [InlineData("SendTestEmail")]
    public void OutboundEndpoint_HasOutboundFetchPolicy(string endpointName)
    {
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == endpointName);

        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
            .Should().Be(RateLimitPolicies.OutboundFetch);
    }
}
