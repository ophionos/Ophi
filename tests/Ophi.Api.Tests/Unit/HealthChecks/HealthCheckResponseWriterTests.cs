using FluentAssertions;
using Ophi.Api.Common.HealthChecks;

namespace Ophi.Api.Tests.Unit.HealthChecks;

public class HealthCheckResponseWriterTests
{
    [Fact]
    public void FormatUptime_Days_FormatsCorrectly()
    {
        var uptime = new TimeSpan(2, 5, 30, 0);

        var result = HealthCheckResponseWriter.FormatUptime(uptime);

        result.Should().Be("2d 5h 30m");
    }

    [Fact]
    public void FormatUptime_HoursOnly_FormatsCorrectly()
    {
        var uptime = new TimeSpan(0, 3, 15, 0);

        var result = HealthCheckResponseWriter.FormatUptime(uptime);

        result.Should().Be("3h 15m");
    }

    [Fact]
    public void FormatUptime_MinutesOnly_FormatsCorrectly()
    {
        var uptime = new TimeSpan(0, 0, 7, 42);

        var result = HealthCheckResponseWriter.FormatUptime(uptime);

        result.Should().Be("7m 42s");
    }

    [Fact]
    public void FormatUptime_ZeroMinutes_FormatsCorrectly()
    {
        var uptime = new TimeSpan(0, 0, 0, 30);

        var result = HealthCheckResponseWriter.FormatUptime(uptime);

        result.Should().Be("0m 30s");
    }

    [Fact]
    public void FormatUptime_ExactlyOneDay_FormatsWithDays()
    {
        var uptime = new TimeSpan(1, 0, 0, 0);

        var result = HealthCheckResponseWriter.FormatUptime(uptime);

        result.Should().Be("1d 0h 0m");
    }
}
