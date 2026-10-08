using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Moq;
using Ophi.Api.Common;

namespace Ophi.Api.Tests.Unit.Common;

public class MetricsEndpointPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsEnabled_InProduction_WithoutToken_ReturnsFalse(string? token)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");

        MetricsEndpointPolicy.IsEnabled(env.Object, token).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_InProduction_WithToken_ReturnsTrue()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");

        MetricsEndpointPolicy.IsEnabled(env.Object, "secret-value").Should().BeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void IsEnabled_OutsideProduction_WithoutToken_ReturnsTrue(string environmentName)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        MetricsEndpointPolicy.IsEnabled(env.Object, null).Should().BeTrue();
    }
}
