using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Moq;
using Ophi.Api.Common;

namespace Ophi.Api.Tests.Unit.Common;

public class MetricsAuthGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureMetricsTokenConfigured_InProduction_WithoutToken_Throws(string? token)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");

        var act = () => MetricsAuthGuard.EnsureMetricsTokenConfigured(env.Object, token);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*MetricsToken*Production*");
    }

    [Fact]
    public void EnsureMetricsTokenConfigured_InProduction_WithToken_DoesNotThrow()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");

        var act = () => MetricsAuthGuard.EnsureMetricsTokenConfigured(env.Object, "secret-value");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void EnsureMetricsTokenConfigured_OutsideProduction_WithoutToken_DoesNotThrow(string environmentName)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        var act = () => MetricsAuthGuard.EnsureMetricsTokenConfigured(env.Object, null);

        act.Should().NotThrow();
    }
}
