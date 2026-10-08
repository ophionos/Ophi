using FluentAssertions;
using Ophi.Domain.Entities;

namespace Ophi.Domain.Tests;

public class NotificationTitleTests
{
    [Fact]
    public void BuildTitle_WithShortName_ReturnsLabelAndNameUnchanged()
    {
        var title = Notification.BuildTitle("Out of stock", "AirPods");

        title.Should().Be("Out of stock: AirPods");
    }

    [Fact]
    public void BuildTitle_WithLongName_TruncatesNameWithEllipsisWithinLimit()
    {
        // Regression: a ~190-char Amazon product name + "Out of stock: " prefix overflowed the
        // Title varchar(200) column and rolled back the whole scrape save.
        var longName = new string('x', 500);

        var title = Notification.BuildTitle("Out of stock", longName);

        title.Length.Should().Be(Notification.TitleMaxLength);
        title.Should().StartWith("Out of stock: x");
        title.Should().EndWith("…");
    }

    [Fact]
    public void BuildTitle_WhenCutFallsInsideASurrogatePair_DoesNotSplitIt()
    {
        // An emoji is two UTF-16 chars. Cutting between them leaves a lone high surrogate, which
        // is invalid UTF-16 and cannot be encoded for the DB or for JSON.
        const string label = "Out of stock";
        var keep = Notification.TitleMaxLength - $"{label}: ".Length - 1;
        var name = new string('x', keep - 1) + "😀" + new string('x', 50);

        var title = Notification.BuildTitle(label, name);

        title.Length.Should().BeLessThanOrEqualTo(Notification.TitleMaxLength);
        title.Should().EndWith("x…");
        char.IsHighSurrogate(title[^2]).Should().BeFalse();
    }

    [Fact]
    public void BuildTitle_AtExactLimit_IsNotTruncated()
    {
        const string label = "Out of stock";
        var prefixLen = $"{label}: ".Length;
        var name = new string('y', Notification.TitleMaxLength - prefixLen); // fills exactly to 200

        var title = Notification.BuildTitle(label, name);

        title.Length.Should().Be(Notification.TitleMaxLength);
        title.Should().NotEndWith("…");
        title.Should().Be($"{label}: {name}");
    }

    [Fact]
    public void BuildTitle_OneOverLimit_TruncatesToExactlyLimit()
    {
        const string label = "Out of stock";
        var prefixLen = $"{label}: ".Length;
        var name = new string('z', Notification.TitleMaxLength - prefixLen + 1); // one char too long

        var title = Notification.BuildTitle(label, name);

        title.Length.Should().Be(Notification.TitleMaxLength);
        title.Should().EndWith("…");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BuildTitle_WithNullOrEmptyName_DoesNotThrow(string? name)
    {
        var title = Notification.BuildTitle("Price alert", name);

        title.Should().Be("Price alert: ");
        title.Length.Should().BeLessThanOrEqualTo(Notification.TitleMaxLength);
    }

    [Fact]
    public void BuildTitle_NeverExceedsTitleMaxLength()
    {
        foreach (var label in new[] { "Out of stock", "Price alert", "Back in stock", "Suspicious scrape", "Scrape error", "URL paused" })
        {
            foreach (var len in new[] { 0, 1, 150, 186, 187, 199, 200, 500 })
            {
                var title = Notification.BuildTitle(label, new string('a', len));
                title.Length.Should().BeLessThanOrEqualTo(Notification.TitleMaxLength,
                    "label '{0}' with a {1}-char name must fit the column", label, len);
            }
        }
    }
}
