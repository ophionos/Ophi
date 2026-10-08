using System.Threading.Channels;
using FluentAssertions;
using Ophi.Api.Common.Events;

namespace Ophi.Api.Tests.Unit.Common;

public class SseConnectionRegistryTests
{
    private readonly SseConnectionRegistry _registry = new();

    [Fact]
    public async Task PublishAsync_DeliversPayload_ToRegisteredConnection()
    {
        var userId = Guid.NewGuid();
        var connection = _registry.Register(userId);

        await _registry.PublishAsync(userId, "hello");

        connection.Reader.TryRead(out var payload).Should().BeTrue();
        payload.Should().Be("hello");
    }

    [Fact]
    public async Task PublishAsync_DeliversToAllConnections_ForSameUser()
    {
        var userId = Guid.NewGuid();
        var first = _registry.Register(userId);
        var second = _registry.Register(userId);

        await _registry.PublishAsync(userId, "ping");

        first.Reader.TryRead(out var a).Should().BeTrue();
        second.Reader.TryRead(out var b).Should().BeTrue();
        a.Should().Be("ping");
        b.Should().Be("ping");
    }

    [Fact]
    public async Task PublishAsync_DoesNotDeliver_ToOtherUsers()
    {
        var target = Guid.NewGuid();
        var other = Guid.NewGuid();
        _registry.Register(target);
        var otherConnection = _registry.Register(other);

        await _registry.PublishAsync(target, "secret");

        otherConnection.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task PublishAsync_ToUserWithNoConnections_IsNoOp()
    {
        var act = async () => await _registry.PublishAsync(Guid.NewGuid(), "nobody-listening");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Unregister_StopsDelivery_AndCompletesTheChannel()
    {
        var userId = Guid.NewGuid();
        var connection = _registry.Register(userId);

        _registry.Unregister(userId, connection.Id);
        await _registry.PublishAsync(userId, "after-unregister");

        connection.Reader.TryRead(out _).Should().BeFalse();
        connection.Reader.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Unregister_OneConnection_LeavesTheOtherReceiving()
    {
        var userId = Guid.NewGuid();
        var staying = _registry.Register(userId);
        var leaving = _registry.Register(userId);

        _registry.Unregister(userId, leaving.Id);
        await _registry.PublishAsync(userId, "still-here");

        staying.Reader.TryRead(out var payload).Should().BeTrue();
        payload.Should().Be("still-here");
        leaving.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Register_ConcurrentWithLastUnregister_ConnectionStillReceivesPublishes()
    {
        // Register racing the Unregister that empties the bucket must never leave the new connection
        // in a bucket that is no longer in the registry (it would stay open but receive nothing).
        for (var i = 0; i < 2_000; i++)
        {
            var userId = Guid.NewGuid();
            var leaving = _registry.Register(userId);
            SseConnection? joining = null;

            await Task.WhenAll(
                Task.Run(() => _registry.Unregister(userId, leaving.Id), TestContext.Current.CancellationToken),
                Task.Run(() => joining = _registry.Register(userId), TestContext.Current.CancellationToken));

            await _registry.PublishAsync(userId, "ping");

            joining!.Reader.TryRead(out var payload).Should().BeTrue($"iteration {i} lost the connection");
            payload.Should().Be("ping");
        }
    }
}
