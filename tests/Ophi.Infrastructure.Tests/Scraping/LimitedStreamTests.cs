using FluentAssertions;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Infrastructure.Tests.Scraping;

public class LimitedStreamTests
{
    [Fact]
    public async Task ReadAsync_WithinLimit_ReturnsAllData()
    {
        var data = new byte[100];
        Array.Fill(data, (byte)'A');
        using var inner = new MemoryStream(data);
        await using var limited = new LimitedStream(inner, 200);

        var buffer = new byte[200];
        var bytesRead = await limited.ReadAsync(buffer, TestContext.Current.CancellationToken);

        bytesRead.Should().Be(100);
    }

    [Fact]
    public async Task ReadAsync_ExceedingLimit_ThrowsIOException()
    {
        var data = new byte[200];
        Array.Fill(data, (byte)'A');
        using var inner = new MemoryStream(data);
        await using var limited = new LimitedStream(inner, 100);

        var buffer = new byte[200];
        var act = async () => await limited.ReadAsync(buffer, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<IOException>()
            .WithMessage("*size limit*");
    }

    [Fact]
    public async Task ReadAsync_ExactlyAtLimit_DoesNotThrow()
    {
        var data = new byte[100];
        Array.Fill(data, (byte)'A');
        using var inner = new MemoryStream(data);
        await using var limited = new LimitedStream(inner, 100);

        var buffer = new byte[100];
        var bytesRead = await limited.ReadAsync(buffer, TestContext.Current.CancellationToken);

        bytesRead.Should().Be(100);
    }
}
