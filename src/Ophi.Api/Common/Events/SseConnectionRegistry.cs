using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Ophi.Api.Common.Events;

/// <summary>
/// A single open SSE connection: its id (for unregistering) and the reader the streaming endpoint
/// drains to write <c>data:</c> frames to the browser.
/// </summary>
public sealed record SseConnection(Guid Id, ChannelReader<string> Reader);

/// <summary>
/// In-memory registry of open Server-Sent-Events connections, keyed by user. The SSE endpoint
/// registers a connection on subscribe and unregisters on disconnect; the <c>LiveUpdate</c> fan-out
/// handler publishes ready-to-send frames to every connection a given user has open.
///
/// <para>
/// Process-local by design — it tracks the streams this API instance hosts. Cross-process delivery
/// (Worker → this API) is handled upstream by the Wolverine transport; see
/// <c>WolverineConfig.ScrapeNotificationQueue</c> for the single-replica constraint.
/// </para>
/// </summary>
public interface ISseConnectionRegistry
{
    /// <summary>Opens a connection for <paramref name="userId"/> and returns its id + frame reader.</summary>
    SseConnection Register(Guid userId);

    /// <summary>Closes the connection, completing its channel so the streaming loop ends.</summary>
    void Unregister(Guid userId, Guid connectionId);

    /// <summary>Writes <paramref name="payload"/> to every open connection for the user; no-op if none.</summary>
    ValueTask PublishAsync(Guid userId, string payload);
}

/// <inheritdoc />
public sealed class SseConnectionRegistry : ISseConnectionRegistry
{
    // Bounded + DropOldest: a thin "refetch" ping is coalescible, so under backpressure we'd rather
    // drop the oldest stale ping than block a publisher or grow unboundedly. SingleWriter is false
    // because concurrent LiveUpdate fan-outs for the same user can write the same channel.
    private static readonly BoundedChannelOptions ChannelOptions = new(64)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    };

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<string>>> _byUser = new();

    // Serialises bucket creation/removal so a Register cannot add to a bucket that a concurrent
    // Unregister is dropping (the connection would stay open but never receive a publish). Connect and
    // disconnect are rare; PublishAsync stays lock-free.
    private readonly Lock _bucketLock = new();

    public SseConnection Register(Guid userId)
    {
        var channel = Channel.CreateBounded<string>(ChannelOptions);
        var connectionId = Guid.NewGuid();
        lock (_bucketLock)
        {
            var connections = _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Channel<string>>());
            connections[connectionId] = channel;
        }

        return new SseConnection(connectionId, channel.Reader);
    }

    public void Unregister(Guid userId, Guid connectionId)
    {
        Channel<string>? channel;
        lock (_bucketLock)
        {
            if (!_byUser.TryGetValue(userId, out var connections))
                return;

            connections.TryRemove(connectionId, out channel);

            // Drop the user bucket once empty so the dictionary doesn't accumulate idle keys.
            if (connections.IsEmpty)
                _byUser.TryRemove(userId, out _);
        }

        channel?.Writer.TryComplete();
    }

    public ValueTask PublishAsync(Guid userId, string payload)
    {
        if (_byUser.TryGetValue(userId, out var connections))
        {
            foreach (var channel in connections.Values)
            {
                // TryWrite never blocks on a DropOldest bounded channel; false means the connection was
                // already completed (mid-unregister) — harmless to skip.
                channel.Writer.TryWrite(payload);
            }
        }

        return ValueTask.CompletedTask;
    }
}
