using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Infrastructure.Tests.Scraping;

[CollectionDefinition(nameof(PlaywrightDriverProcesses), DisableParallelization = true)]
public class PlaywrightDriverProcesses;

/// <summary>
/// #132: a recycle only bounds memory if it ends the old node driver process. Runs alone, so no
/// other test's driver shows up in the process snapshot. Needs Playwright installed.
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(PlaywrightDriverProcesses))]
public class PlaywrightDriverRecycleTests
{
    [Fact]
    public async Task Recycle_EndsTheOldNodeDriverAndStartsANewOne()
    {
        var before = DriverPids();
        await using var manager = new PlaywrightBrowserManager(
            NullLogger<PlaywrightBrowserManager>.Instance, maxContextsPerBrowser: 1);

        var first = await manager.NewPageAsync();
        await first.Context.CloseAsync();
        var firstDriver = DriverPids().Except(before).Should().ContainSingle().Subject;

        var second = await manager.NewPageAsync();
        await second.Context.CloseAsync();

        (await ExitsWithinAsync(firstDriver, TimeSpan.FromSeconds(10))).Should().BeTrue("the recycle must end the old driver");
        DriverPids().Except(before).Should().ContainSingle().Which.Should().NotBe(firstDriver);
    }

    /// <summary>The Playwright node drivers: node processes started from a <c>.playwright</c> folder.</summary>
    private static HashSet<int> DriverPids()
    {
        var pids = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("node"))
        {
            try
            {
                if (process.MainModule?.FileName.Contains(".playwright", StringComparison.OrdinalIgnoreCase) == true)
                    pids.Add(process.Id);
            }
            catch
            {
                // Exited, or not ours to inspect.
            }
            finally
            {
                process.Dispose();
            }
        }
        return pids;
    }

    private static async Task<bool> ExitsWithinAsync(int pid, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!DriverPids().Contains(pid)) return true;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        return false;
    }
}
