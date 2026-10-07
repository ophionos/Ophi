using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Infrastructure.Tests.Scraping;

/// <summary>
/// Integration tests for PlaywrightBrowserManager.
/// These tests require Playwright to be installed.
/// </summary>
[Trait("Category", "Integration")]
public class PlaywrightBrowserManagerTests
{
    private readonly Mock<ILogger<PlaywrightBrowserManager>> _loggerMock;

    public PlaywrightBrowserManagerTests()
    {
        _loggerMock = new Mock<ILogger<PlaywrightBrowserManager>>();
    }

    [Fact]
    public async Task NewPageAsync_InitializesBrowser()
    {
        // Arrange
        await using var manager = new PlaywrightBrowserManager(_loggerMock.Object);

        // Act
        var page = await manager.NewPageAsync();

        // Assert
        page.Should().NotBeNull();
        await page.CloseAsync();
    }

    [Fact]
    public async Task NewPageAsync_ReusesBrowser()
    {
        // Arrange
        await using var manager = new PlaywrightBrowserManager(_loggerMock.Object);

        // Act - Create multiple pages
        var page1 = await manager.NewPageAsync();
        var page2 = await manager.NewPageAsync();

        // Assert - Both pages should be valid (reusing same browser)
        page1.Should().NotBeNull();
        page2.Should().NotBeNull();
        page1.Should().NotBeSameAs(page2);

        await page1.CloseAsync();
        await page2.CloseAsync();
    }

    [Fact]
    public async Task NewPageAsync_IsThreadSafe()
    {
        // Arrange
        await using var manager = new PlaywrightBrowserManager(_loggerMock.Object);
        var tasks = new List<Task<Microsoft.Playwright.IPage>>();

        // Act - Create multiple pages concurrently
        for (var i = 0; i < 5; i++)
        {
            tasks.Add(manager.NewPageAsync());
        }

        var pages = await Task.WhenAll(tasks);

        // Assert - All pages should be valid
        pages.Should().HaveCount(5);
        pages.Should().AllSatisfy(p => p.Should().NotBeNull());

        // Cleanup
        foreach (var page in pages)
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task DisposeAsync_ClosesBrowser()
    {
        // Arrange
        var manager = new PlaywrightBrowserManager(_loggerMock.Object);
        var page = await manager.NewPageAsync();
        await page.CloseAsync();

        // Act
        await manager.DisposeAsync();

        // Assert - Subsequent calls should throw
        var act = async () => await manager.NewPageAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task ThrowsAfterDisposal()
    {
        // Arrange
        var manager = new PlaywrightBrowserManager(_loggerMock.Object);
        await manager.DisposeAsync();

        // Act & Assert
        var act = async () => await manager.NewPageAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        var manager = new PlaywrightBrowserManager(_loggerMock.Object);
        var page = await manager.NewPageAsync();
        await page.CloseAsync();

        // Act - Call dispose multiple times
        await manager.DisposeAsync();
        await manager.DisposeAsync(); // Should not throw

        // Assert - passed if no exception
    }

    [Fact]
    public async Task NewPageAsync_PageCanNavigate()
    {
        // Arrange
        await using var manager = new PlaywrightBrowserManager(_loggerMock.Object);
        var page = await manager.NewPageAsync();

        try
        {
            // Act - Navigate to a simple data URL
            await page.GotoAsync("data:text/html,<html><body>Hello</body></html>");

            // Assert
            var content = await page.ContentAsync();
            content.Should().Contain("Hello");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
