using FluentAssertions;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Infrastructure.Tests.Scraping;

public class BrowserProfileTests
{
    // Use IEnumerable<object[]> so the internal BrowserProfile type isn't exposed
    // in a public method signature (which would be CS0050/CS0051)
    public static IEnumerable<object[]> AllProfiles() =>
        BrowserProfiles.GetAll().Select(p => new object[] { p });

    public static IEnumerable<object[]> ChromiumProfiles() =>
        BrowserProfiles.GetAll().Where(p => p.IsChromium).Select(p => new object[] { p });

    public static IEnumerable<object[]> NonChromiumProfiles() =>
        BrowserProfiles.GetAll().Where(p => !p.IsChromium).Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void BrowserProfile_HasNonEmptyRequiredFields(object profileObj)
    {
        var profile = (BrowserProfile)profileObj;
        profile.UserAgent.Should().NotBeNullOrWhiteSpace();
        profile.Accept.Should().NotBeNullOrWhiteSpace();
        profile.AcceptLanguage.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(ChromiumProfiles))]
    public void BrowserProfile_ChromiumVariant_HasSecChUaHeaders(object profileObj)
    {
        var profile = (BrowserProfile)profileObj;
        profile.SecChUa.Should().NotBeNullOrWhiteSpace();
        profile.SecChUaMobile.Should().NotBeNullOrWhiteSpace();
        profile.SecChUaPlatform.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(NonChromiumProfiles))]
    public void BrowserProfile_NonChromiumVariant_HasNoSecChUaHeaders(object profileObj)
    {
        var profile = (BrowserProfile)profileObj;
        profile.SecChUa.Should().BeNull();
        profile.SecChUaMobile.Should().BeNull();
        profile.SecChUaPlatform.Should().BeNull();
    }

    [Fact]
    public void GetAll_ReturnsNonEmptyList()
    {
        BrowserProfiles.GetAll().Should().NotBeEmpty();
    }

    [Fact]
    public void GetRandom_ReturnsValidProfile()
    {
        var profile = BrowserProfiles.GetRandom();
        profile.Should().NotBeNull();
        profile.UserAgent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetAll_HasBothChromiumAndNonChromiumProfiles()
    {
        var all = BrowserProfiles.GetAll();
        all.Any(p => p.IsChromium).Should().BeTrue("should include at least one Chromium profile");
        all.Any(p => !p.IsChromium).Should().BeTrue("should include at least one non-Chromium profile");
    }
}
