using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ophi.Infrastructure.Tests;

public class DependencyInjectionTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void AddInfrastructure_WithNoSmtpConfig_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>());

        var act = () => services.AddInfrastructure(config);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddInfrastructure_WithAllSmtpFields_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SMTP_HOST"] = "smtp.example.com",
            ["SMTP_USER"] = "user@example.com",
            ["SMTP_PASS"] = "secret",
            ["SMTP_FROM"] = "noreply@example.com"
        });

        var act = () => services.AddInfrastructure(config);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("SMTP_USER")]
    [InlineData("SMTP_PASS")]
    [InlineData("SMTP_FROM")]
    public void AddInfrastructure_WithSmtpHostButMissingField_Throws(string missingKey)
    {
        var services = new ServiceCollection();
        var values = new Dictionary<string, string?>
        {
            ["SMTP_HOST"] = "smtp.example.com",
            ["SMTP_USER"] = "user@example.com",
            ["SMTP_PASS"] = "secret",
            ["SMTP_FROM"] = "noreply@example.com",
            [missingKey] = null
        };
        var config = BuildConfig(values);

        var act = () => services.AddInfrastructure(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{missingKey}*");
    }

    [Fact]
    public void AddInfrastructure_WithSmtpHostOnly_ThrowsListingAllMissingFields()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SMTP_HOST"] = "smtp.example.com"
        });

        var act = () => services.AddInfrastructure(config);

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("SMTP_USER")
            .And.Contain("SMTP_PASS")
            .And.Contain("SMTP_FROM");
    }
}
