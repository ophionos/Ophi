namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Wraps a stream and throws if more than <paramref name="maxBytes"/> are read.
/// </summary>
internal sealed class LimitedStream(Stream inner, long maxBytes) : Stream
{
    private long _totalRead;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = inner.Read(buffer, offset, count);
        _totalRead += bytesRead;
        if (_totalRead > maxBytes)
            throw new IOException($"Response body exceeded the {maxBytes / (1024 * 1024)} MB size limit.");
        return bytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var bytesRead = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        _totalRead += bytesRead;
        if (_totalRead > maxBytes)
            throw new IOException($"Response body exceeded the {maxBytes / (1024 * 1024)} MB size limit.");
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytesRead = await inner.ReadAsync(buffer, cancellationToken);
        _totalRead += bytesRead;
        if (_totalRead > maxBytes)
            throw new IOException($"Response body exceeded the {maxBytes / (1024 * 1024)} MB size limit.");
        return bytesRead;
    }

    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
