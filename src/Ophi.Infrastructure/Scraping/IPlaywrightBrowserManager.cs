using Microsoft.Playwright;

namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Interface for managing Playwright browser instances.
/// Enables mocking for unit tests.
/// </summary>
public interface IPlaywrightBrowserManager : IAsyncDisposable
{
    /// <summary>
    /// Gets a new page from the browser with the specified User-Agent.
    /// When null, a randomly selected browser profile is used.
    /// Initializes the browser if needed.
    /// </summary>
    Task<IPage> NewPageAsync(string? userAgent = null);
}
