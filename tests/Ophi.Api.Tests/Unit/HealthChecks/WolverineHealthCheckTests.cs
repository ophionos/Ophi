using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Ophi.Api.Common.HealthChecks;
using Wolverine;

namespace Ophi.Api.Tests.Unit.HealthChecks;

public class WolverineHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenMessageBusRegistered_ReturnsHealthy()
    {
        var mockBus = new Mock<IMessageBus>();
        var mockProvider = new Mock<IServiceProvider>();
        mockProvider.Setup(p => p.GetService(typeof(IMessageBus))).Returns(mockBus.Object);

        var check = new WolverineHealthCheck(mockProvider.Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("available");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenMessageBusNotRegistered_ReturnsUnhealthy()
    {
        var mockProvider = new Mock<IServiceProvider>();
        mockProvider.Setup(p => p.GetService(typeof(IMessageBus))).Returns(null!);

        var check = new WolverineHealthCheck(mockProvider.Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("not registered");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenServiceResolutionThrows_ReturnsUnhealthy()
    {
        var mockProvider = new Mock<IServiceProvider>();
        mockProvider.Setup(p => p.GetService(typeof(IMessageBus)))
            .Throws(new InvalidOperationException("DI failure"));

        var check = new WolverineHealthCheck(mockProvider.Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }
}
